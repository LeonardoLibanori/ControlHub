import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../wwwroot/js/admin-caixa.js', import.meta.url), 'utf8');
const startMarker = "const CHAVE_VENDA_PENDENTE_STORAGE = 'controlhub.caixa.venda-pendente.v1';";
const endMarker = "window.addEventListener('load', restaurarVendaPendenteAoAbrir);";
const start = source.indexOf(startMarker);
const end = source.indexOf(endMarker, start);
assert.notEqual(start, -1, 'helpers de idempotência encontrados no JS do caixa');
assert.notEqual(end, -1, 'handler de reload encontrado no JS do caixa');
const sourceUnderTest = source.slice(start, end + endMarker.length);

function criarAmbiente({ storage = new Map(), fetch = async () => { throw new Error('fetch não configurado'); }, swal = [], onSwal = () => {} } = {}) {
    const eventos = {};
    const alertas = [];
    const chamadas = [];
    const botoesFinalizar = [{ disabled: false }];
    const metodos = [{ disabled: false }];
    const elementos = new Map([
        ['valorRecebidoInput', { value: '20', readOnly: false }],
        ['btnConfirmar', { disabled: false }],
        ['btnConfirmarPagamento', { disabled: false }],
        ['cpfClienteInput', { value: '' }],
        ['carrinho', { innerHTML: '' }]
    ]);
    const localizacao = { pathname: '/Caixa', href: '/Caixa', reloadCount: 0, reload() { this.reloadCount++; } };
    const window = {
        crypto: { randomUUID: () => 'b375acfe-445a-4a73-9a62-ec9f30e40e10' },
        location: localizacao,
        addEventListener: (nome, handler) => { eventos[nome] = handler; },
        open: (...args) => chamadas.push(['open', ...args]),
        vendaEnviando: false
    };
    const document = {
        querySelector: () => null,
        querySelectorAll: selector => selector === 'button[onclick="enviarVendaDeVez()"]'
            ? botoesFinalizar
            : selector === '.btn-metodo-sel' ? metodos : [],
        getElementById: id => elementos.get(id) || null
    };
    const sandbox = {
        window, document, sessionStorage: {
            getItem: key => storage.has(key) ? storage.get(key) : null,
            setItem: (key, value) => storage.set(key, value),
            removeItem: key => storage.delete(key)
        },
        location: localizacao,
        carrinho: undefined,
        fetch: async (...args) => { chamadas.push(['fetch', ...args]); return fetch(...args); },
        Swal: { fire: async options => { alertas.push(options); onSwal(options); return swal.shift() || { isConfirmed: false }; } },
        csrfHeaders: extra => extra || {},
        atualizarTokenCsrf: async () => false,
        renderCarrinho: () => {},
        calcularTroco: () => {},
        URLSearchParams,
        encodeURIComponent,
        console: { error() {} }
    };
    vm.createContext(sandbox);
    vm.runInContext('let carrinho = []; let metodoSelecionado = "Pix"; let codigoVendedorInformado = "";', sandbox);
    vm.runInContext(sourceUnderTest, sandbox, { filename: 'admin-caixa.js:pending-flow' });
    return { sandbox, storage, eventos, alertas, chamadas, botoesFinalizar, elementos };
}

function estadoPendente(env) {
    return JSON.parse(env.storage.get('controlhub.caixa.venda-pendente.v1'));
}

test('reload consulta status antes de retomar; restaura chave/carrinho sem persistir CPF', async () => {
    const storage = new Map([['controlhub.caixa.venda-pendente.v1', JSON.stringify({
        chave: 'e230a7b4-4593-416c-93f3-bcaa646193fd', metodo: 'Pix', codigoVendedor: 'CX7',
        valorRecebido: '0', cpfInformado: false,
        itens: [{ ProdutoId: '42', Quantidade: '2', Preco: '8', nome: 'Café', unidade: 'un', qtd: 2, preco: 8, subtotal: 16, estoque: 4 }]
    })]]);
    const env = criarAmbiente({ storage, swal: [{ isConfirmed: true }], fetch: async (url, options) => {
        if (String(url).includes('ConsultarStatusVenda')) return { ok: true, json: async () => ({ success: true, encontrada: false }) };
        return { ok: false, status: 503, text: async () => 'busy' };
    }});

    await env.eventos.load();

    assert.equal(env.chamadas.filter(call => call[0] === 'fetch').length, 2);
    assert.match(env.chamadas[0][1], /ConsultarStatusVenda/);
    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), true);
    assert.equal(env.sandbox.window.vendaPendenteAtiva, true);
    assert.equal(vm.runInContext('carrinho[0].ProdutoId', env.sandbox), '42');
    assert.equal(vm.runInContext('carrinho[0].Quantidade', env.sandbox), '2');
    const post = new URLSearchParams(env.chamadas[1][2].body);
    assert.equal(post.get('chaveIdempotencia'), 'e230a7b4-4593-416c-93f3-bcaa646193fd');
    assert.equal(post.get('itens[0].ProdutoId'), '42');
    assert.equal(post.get('itens[0].Quantidade'), '2');
    assert.equal(Object.hasOwn(estadoPendente(env), 'cpfCliente'), false);
    assert.equal(env.alertas[0].title, 'Venda anterior sem confirmação');
});

test('status encontrado após reload confirma a venda sem reenviar POST e limpa estado só após confirmação', async () => {
    const storage = new Map([['controlhub.caixa.venda-pendente.v1', JSON.stringify({
        chave: '2ef0ae8c-f788-41d2-8c74-adf23f656805', metodo: 'Pix', codigoVendedor: '', valorRecebido: '0', cpfInformado: false,
        itens: [{ ProdutoId: '8', Quantidade: '1', Preco: '5', nome: 'Arroz', unidade: 'un', qtd: 1, preco: 5, subtotal: 5 }]
    })]]);
    let liberarConfirmacao;
    let indicarConfirmacaoAberta;
    const confirmacaoAberta = new Promise(resolve => { indicarConfirmacaoAberta = resolve; });
    const env = criarAmbiente({ storage, swal: [new Promise(resolve => { liberarConfirmacao = resolve; })],
        onSwal: options => { if (options.title === 'Venda confirmada') indicarConfirmacaoAberta(); },
        fetch: async () => ({ ok: true, json: async () => ({ success: true, encontrada: true, vendaId: 91 }) }) });

    const recuperacao = env.eventos.load();
    await confirmacaoAberta;

    assert.equal(env.chamadas.filter(call => call[0] === 'fetch').length, 1);
    assert.match(env.chamadas[0][1], /ConsultarStatusVenda/);
    assert.equal(storage.has('controlhub.caixa.venda-pendente.v1'), true);
    assert.equal(env.elementos.get('valorRecebidoInput').readOnly, true);
    liberarConfirmacao({ isConfirmed: true });
    await recuperacao;
    assert.equal(storage.has('controlhub.caixa.venda-pendente.v1'), false);
    assert.equal(env.chamadas.some(call => call[0] === 'open' && String(call[1]).includes('id=91')), true);
    assert.equal(env.elementos.get('valorRecebidoInput').readOnly, false);
});

test('falha de comunicação preserva a mesma chave e o mesmo conteúdo para nova tentativa', async () => {
    const bodies = [];
    let falhar = true;
    const env = criarAmbiente({ fetch: async (_url, options) => {
        bodies.push(options.body.toString());
        if (falhar) throw new TypeError('offline');
        return { ok: true, status: 200, json: async () => ({ success: true, vendaId: 31 }) };
    }, swal: [{ isConfirmed: true }] });
    vm.runInContext('carrinho = [{ ProdutoId: 6, id: 6, Quantidade: "2", qtd: 2, Preco: "4.50", preco: 4.5, nome: "Feijão", unidade: "un", subtotal: 9, estoque: 5 }];', env.sandbox);

    await env.sandbox.enviarVendaSegura('123.456.789-00');
    const original = estadoPendente(env);
    assert.equal(env.storage.get('controlhub.caixa.venda-pendente.v1').includes('12345678900'), false);
    falhar = false;
    await env.sandbox.enviarVendaSegura('123.456.789-00');

    assert.equal(original.chave, 'b375acfe-445a-4a73-9a62-ec9f30e40e10');
    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), false);
    assert.equal(bodies.length, 2);
    assert.equal(bodies[0], bodies[1]);
    assert.equal(new URLSearchParams(bodies[0]).get('cpfCliente'), '123.456.789-00');
    assert.match(env.alertas[0].title, /Confirmação pendente/);
    assert.match(env.alertas[0].text, /pode ter sido gravada/);
});

test('reload com CPF solicita nova digitação e não recupera CPF do armazenamento', async () => {
    const key = 'e9ae2df6-0c08-44f7-ab3f-789e2ca2cd61';
    const storage = new Map([['controlhub.caixa.venda-pendente.v1', JSON.stringify({
        chave: key, metodo: 'Pix', codigoVendedor: '', valorRecebido: '0', cpfInformado: true,
        itens: [{ ProdutoId: '14', Quantidade: '1', Preco: '4', nome: 'Açúcar', qtd: 1, preco: 4 }]
    })]]);
    const env = criarAmbiente({ storage, swal: [
        { isConfirmed: true },
        { isConfirmed: true, value: '123.456.789-00' }
    ], fetch: async url => {
        if (String(url).includes('ConsultarStatusVenda')) throw new TypeError('status indisponível');
        return { ok: false, status: 503, text: async () => 'busy' };
    }});

    await env.eventos.load();

    const post = new URLSearchParams(env.chamadas[1][2].body);
    assert.equal(post.get('chaveIdempotencia'), key);
    assert.equal(post.get('cpfCliente'), '123.456.789-00');
    assert.equal(env.storage.get('controlhub.caixa.venda-pendente.v1').includes('12345678900'), false);
    assert.equal(env.alertas[1].title, 'CPF usado na tentativa anterior');
    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), true);
});

test('HTTP 503 mantém estado pendente e retry usa a chave original', async () => {
    const keys = [];
    let status = 503;
    const env = criarAmbiente({ fetch: async (_url, options) => {
        const body = new URLSearchParams(options.body);
        keys.push(body.get('chaveIdempotencia'));
        return status === 503
            ? { ok: false, status, text: async () => 'busy' }
            : { ok: true, status: 200, json: async () => ({ success: true, vendaId: 33 }) };
    }, swal: [{ isConfirmed: true }] });
    vm.runInContext('carrinho = [{ ProdutoId: 2, Quantidade: "1", Preco: "3", qtd: 1, preco: 3, nome: "Leite" }];', env.sandbox);

    await env.sandbox.enviarVendaSegura();
    const key = estadoPendente(env).chave;
    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), true);
    assert.equal(env.alertas[0].title, 'Confirmação pendente');
    assert.match(env.alertas[0].text, /temporariamente ocupado ou indisponível/);
    assert.match(env.alertas[0].text, /resultado pode estar indefinido/);
    status = 200;
    await env.sandbox.enviarVendaSegura();

    assert.deepEqual(keys, [key, key]);
    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), false);
});

test('HTTP 409 preserva chave e carrinho e não anuncia confirmação', async () => {
    const env = criarAmbiente({ fetch: async () => ({ ok: false, status: 409, text: async () => 'conflict' }) });
    vm.runInContext('carrinho = [{ ProdutoId: 9, Quantidade: "1", Preco: "2", qtd: 1, preco: 2, nome: "Sal" }];', env.sandbox);

    await env.sandbox.enviarVendaSegura();

    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), true);
    assert.equal(env.alertas[0].title, 'Conflito na tentativa de venda');
    assert.doesNotMatch(env.alertas[0].text, /confirmada|concluída/i);
});

test('sucesso definitivo limpa o estado e duas chamadas simultâneas enviam apenas um POST', async () => {
    let liberar;
    let posts = 0;
    const env = criarAmbiente({ fetch: async () => {
        posts++;
        return new Promise(resolve => { liberar = resolve; });
    }, swal: [{ isConfirmed: true }] });
    vm.runInContext('carrinho = [{ ProdutoId: 12, Quantidade: "1", Preco: "6", qtd: 1, preco: 6, nome: "Óleo" }];', env.sandbox);

    const primeira = env.sandbox.enviarVendaSegura();
    const duplicada = env.sandbox.enviarVendaSegura();
    await Promise.resolve();
    assert.equal(posts, 1);
    liberar({ ok: true, status: 200, json: async () => ({ success: true, vendaId: 71 }) });
    await Promise.all([primeira, duplicada]);

    assert.equal(env.storage.has('controlhub.caixa.venda-pendente.v1'), false);
    assert.equal(env.chamadas.some(call => call[0] === 'open' && String(call[1]).includes('id=71')), true);
    assert.equal(env.elementos.get('btnConfirmarPagamento').disabled, false);
});
