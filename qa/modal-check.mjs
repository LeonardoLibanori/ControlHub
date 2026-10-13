/* ============================================================================
   modal-check.mjs - guarda dos modais de formulario (COMMIT 2 e 3)
   Dono e Operador, dois temas, 1440x900, 1366x768 e 390x844, com 3 produtos.
   COMMIT 2 (Editar produto) - 15 checagens; COMMIT 3 (Nova conta) - 8;
   COMMIT 4 (Editar conta de usuario) - 13.
   Credenciais SO por ambiente: SHELL_DONO_EMAIL/SENHA, SHELL_OPER_EMAIL/SENHA.
   Uso: node qa/modal-check.mjs --base http://127.0.0.1:5311 --out "...\prints"
   ============================================================================ */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
function arg(nome, padrao) { const i = argv.indexOf(nome); return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao; }
const BASE = arg('--base', 'http://127.0.0.1:5311').replace(/\/+$/, '');
const OUT = arg('--out', join(aqui, '..', '..', 'prints'));
const cred = {
    dono: [process.env.SHELL_DONO_EMAIL, process.env.SHELL_DONO_SENHA],
    operador: [process.env.SHELL_OPER_EMAIL, process.env.SHELL_OPER_SENHA]
};
if (!cred.dono[0] || !cred.dono[1] || !cred.operador[0] || !cred.operador[1]) {
    console.error('modal-check: defina SHELL_DONO_EMAIL/SENHA e SHELL_OPER_EMAIL/SENHA');
    process.exit(1);
}
const espera = (ms) => new Promise(r => setTimeout(r, ms));
let aprov = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { aprov++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
function pular(desc, det) { console.log('  [PULA]  ' + desc + (det !== undefined ? ' | ' + det : '')); }
function ev(api, expr) { return api.avaliar(expr); }
function j(api, expr) { return ev(api, 'JSON.stringify(' + expr + ')').then(JSON.parse); }
async function setValor(api, sel, valor) {
    await ev(api, `(() => {
        const el = document.querySelector(${JSON.stringify(sel)});
        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        set.call(el, ${JSON.stringify(valor)});
        el.dispatchEvent(new Event('input', { bubbles: true }));
        return 'ok';
    })()`);
}
function abrirEditar(api) { return ev(api, `(()=>{const a=document.querySelector('a[data-modal-form]'); if(!a) throw new Error('sem link Editar em '+location.pathname+' busca='+new URLSearchParams(location.search).get('busca')); a.click(); return 'ok';})()`); }
function abrirConta(api) { return ev(api, `(()=>{const a=document.querySelector('a[data-modal-form][data-modal-titulo="Nova conta"]'); if(!a) throw new Error('sem link Nova conta em '+location.pathname); a.click(); return 'ok';})()`); }

// Conta POSTs a uma rota por eventos de REDE do CDP (sobrevive ao reload da
// pagina, ao contrario de um contador em window). Requer Network habilitado.
function criarContadorRede(api, trechoRota) {
    const estado = { n: 0 };
    api.aoEvento((m) => {
        if (m.method === 'Network.requestWillBeSent' && m.params && m.params.request &&
            m.params.request.method === 'POST' && String(m.params.request.url).indexOf(trechoRota) >= 0) {
            estado.n++;
        }
    });
    return estado;
}
function lerContador(estado) { return estado.n; }


async function editarChecks(api, tema) {
    const g = 'editar/' + tema;
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index?busca=')}`);
    await espera(800);
    const colAntes = await j(api, `[...document.querySelectorAll('.tabela-responsiva--estoque thead th')].map(th=>Math.round(th.getBoundingClientRect().width))`);

    // (1) abre o modal com o produto certo, sem navegar
    await abrirEditar(api); await espera(1400);
    let st = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), titulo: (document.querySelector('.app-modal__titulo')||{}).textContent, nome: (document.querySelector('[data-modal-conteudo] input[name=nome]')||{}).value, path: location.pathname + location.search }`);
    check(g + ' (1) modal abre sem navegar', st.overlay === true && st.path.indexOf('/Home/Index') === 0, JSON.stringify(st));
    check(g + ' (1) titulo "Editar produto"', st.titulo === 'Editar produto', st.titulo);
    check(g + ' (1) produto correto carregado', /^ARROZ/.test(st.nome || ''), st.nome);

    // (5) Vendido por troca o rotulo
    await ev(api, `(()=>{const s=document.querySelector('[data-modal-conteudo] #tipoProduto'); s.value='PorPeso'; s.dispatchEvent(new Event('change',{bubbles:true})); return 'ok';})()`); await espera(250);
    let rot = await ev(api, `(document.querySelector('[data-modal-conteudo] #labelPreco')||{}).textContent`);
    check(g + ' (5) PorPeso -> rotulo por kg', /kg/.test(rot || ''), rot);
    await ev(api, `(()=>{const s=document.querySelector('[data-modal-conteudo] #tipoProduto'); s.value='Prateleira'; s.dispatchEvent(new Event('change',{bubbles:true})); return 'ok';})()`); await espera(250);
    rot = await ev(api, `(document.querySelector('[data-modal-conteudo] #labelPreco')||{}).textContent`);
    check(g + ' (5) Unidade -> rotulo unitario', /unit/.test(rot || ''), rot);

    // (6) Voltar fecha e devolve foco
    await ev(api, `(()=>{const c=document.querySelector('[data-modal-conteudo] [data-modal-fechar]'); if(c){c.click(); return 'ok';} return 'sem';})()`); await espera(400);
    st = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), foco: document.activeElement && document.activeElement.className }`);
    check(g + ' (6) Voltar fecha', st.overlay === false, JSON.stringify(st));
    check(g + ' (6) foco volta ao gatilho Editar', /btn-editar/.test(st.foco || ''), st.foco);

    // (7) clique no fundo: limpo fecha, sujo NAO fecha
    await abrirEditar(api); await espera(1300);
    await ev(api, `document.querySelector('.app-modal-overlay').dispatchEvent(new MouseEvent('mousedown',{bubbles:true}))`); await espera(300);
    check(g + ' (7) clique no fundo (limpo) fecha', (await ev(api, `!document.querySelector('.app-modal-overlay')`)) === true);
    await abrirEditar(api); await espera(1300);
    await ev(api, `(()=>{const i=document.querySelector('[data-modal-conteudo] input[name=estoqueMinimo]'); const set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; set.call(i,'7'); i.dispatchEvent(new Event('input',{bubbles:true})); return 'ok';})()`);
    await ev(api, `document.querySelector('.app-modal-overlay').dispatchEvent(new MouseEvent('mousedown',{bubbles:true}))`); await espera(300);
    check(g + ' (7) clique no fundo (sujo) NAO fecha', (await ev(api, `!!document.querySelector('.app-modal-overlay')`)) === true);
    await fecharEsc(api); await espera(300);

    // (8) Tab preso no modal
    await abrirEditar(api); await espera(1300);
    const dentro = await ev(api, `(()=>{ for(let i=0;i<20;i++){ document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Tab',bubbles:true})); } const o=document.querySelector('.app-modal-overlay'); return o && o.contains(document.activeElement); })()`);
    check(g + ' (8) 20 Tabs presos no modal', dentro === true);
    await fecharEsc(api); await espera(300);


    // (2) salvar alterando nome/preco: recarrega, mostra novo, mantem a busca
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index?busca=ARROZ')}`); await espera(800);
    await abrirEditar(api); await espera(1300);
    await setValor(api, '[data-modal-conteudo] input[name=nome]', 'ARROZ EDITADO');
    await ev(api, `(()=>{const v=document.querySelector('[data-modal-conteudo] #precoVisual'); const set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; set.call(v,'30,00'); v.dispatchEvent(new Event('input',{bubbles:true})); return 'ok';})()`); await espera(200);
    await ev(api, `(()=>{const b=[...document.querySelectorAll('[data-modal-conteudo] button')].find(x=>/Salvar/i.test(x.textContent)); if(b) b.click(); return 'ok';})()`); await espera(2800);
    let pos = await j(api, `{ path: location.pathname, overlay: !!document.querySelector('.app-modal-overlay'), busca: (document.querySelector('input[name=busca]')||{}).value, tem: document.body.innerText.indexOf('ARROZ EDITADO')>=0 }`);
    check(g + ' (2) salvou e recarregou (modal fechou)', pos.overlay === false, JSON.stringify(pos));
    check(g + ' (2) nome novo aparece na lista', pos.tem === true);
    check(g + ' (2) manteve a busca', pos.busca === 'ARROZ', pos.busca);

    // (3) preco novo (30) refletido no detalhe
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Produto/1')}`); await espera(700);
    const hist = await ev(api, `document.body.innerText`);
    check(g + ' (3) preco novo (30) no detalhe', /30,00|30/.test(hist));

    // (9) duplo clique no Salvar = 1 POST (contado por eventos de rede do CDP)
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index?busca=ARROZ')}`); await espera(800);
    const contEditar = criarContadorRede(api, '/Home/Editar');
    await abrirEditar(api); await espera(1300);
    await ev(api, `(()=>{const b=[...document.querySelectorAll('[data-modal-conteudo] button')].find(x=>/Salvar/i.test(x.textContent)); if(b){ b.click(); b.click(); } return 'ok';})()`); await espera(2800);
    const posts = lerContador(contEditar);
    check(g + ' (9) duplo clique Salvar = 1 POST', posts === 1, 'posts=' + posts);

    // (10) caixa dentro da janela; sem scroll horizontal
    await espera(1500);
    let caixa = await j(api, `(()=>{ const o=document.querySelector('.app-modal-overlay'); if(!o) return null; const r=o.getBoundingClientRect(); return { dentro: r.left>=-1 && r.top>=-1 && r.right<=window.innerWidth+1 && r.bottom<=window.innerHeight+1, scrollH: document.documentElement.scrollWidth>window.innerWidth }; })()`);
    if (caixa) check(g + ' (10) caixa dentro da janela', caixa.dentro === true, JSON.stringify(caixa));
    if (caixa) check(g + ' (10) sem scroll horizontal', caixa.scrollH === false);

    // (11) GET falho nao trava e mostra "Abrir em tela cheia"
    // Instala o bloqueio DEPOIS de navegar (o location.href recarrega e apagaria o wrapper).
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index?busca=ARROZ')}`); await espera(900);
    await ev(api, `(()=>{ window.__bloq=true; const of=window.fetch; window.fetch=function(u,o){ if(window.__bloq && typeof u==='string' && /\\/Home\\/Editar\\//.test(u) && (!o||!o.method)){ return Promise.reject(new Error('offline')); } return of.apply(this,arguments); }; return 'ok';})()`);
    await ev(api, `document.querySelector('a[data-modal-form]').click()`); await espera(1400);
    let erro = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), texto: (document.querySelector('.app-modal__corpo')||{}).innerText||'' }`);
    check(g + ' (11) GET falho mostra "Abrir em tela cheia"', erro.overlay === true && /tela cheia/i.test(erro.texto), erro.texto.slice(0,50));
    await ev(api, `window.__bloq=false;`); await fecharEsc(api); await espera(300);

    // (15) colunas do Estoque estaveis
    const colDepois = await j(api, `[...document.querySelectorAll('.tabela-responsiva--estoque thead th')].map(th=>Math.round(th.getBoundingClientRect().width))`);
    let colunasOk = true;
    for (let i = 0; i < colAntes.length; i++) { if (colDepois[i] !== undefined && Math.abs(colDepois[i] - colAntes[i]) > 1) colunasOk = false; }
    check(g + ' (15) colunas do Estoque estaveis (+/-1px)', colunasOk, JSON.stringify({ antes: colAntes, depois: colDepois }));
}


async function novaContaChecks(api, tema) {
    const g = 'conta/' + tema;
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Usuarios/Index')}`); await espera(800);
    // COMMIT 2: o gatilho ainda nao esta em modal -> pula os checks de Nova conta.
    // COMMIT 3 (apos ligar o botao) passa a executa-los de verdade.
    const gatilho = await ev(api, `!!document.querySelector('a[data-modal-form][data-modal-titulo="Nova conta"]')`);
    if (!gatilho) { pular(g + ' Nova conta ainda nao em modal (COMMIT 3 pendente)'); return; }
    await abrirConta(api); await espera(1400);
    let st = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), titulo: (document.querySelector('.app-modal__titulo')||{}).textContent, form: !!document.querySelector('[data-modal-conteudo] form'), path: location.pathname }`);
    check(g + ' (1) modal Nova conta abre sem navegar', st.overlay === true && st.form === true && st.path.indexOf('/Usuarios/Index') === 0, JSON.stringify(st));
    check(g + ' (1) titulo "Nova conta"', st.titulo === 'Nova conta', st.titulo);

    await setValor(api, '[data-modal-conteudo] input[name=Nome]', 'Duplicado Teste');
    await setValor(api, '[data-modal-conteudo] input[name=Email]', 'operador@teste.com');
    await setValor(api, '[data-modal-conteudo] input[name=Senha]', 'Duplicado@123');
    await setValor(api, '[data-modal-conteudo] input[name=ConfirmarSenha]', 'Duplicado@123');
    await ev(api, `(()=>{const f=document.querySelector('[data-modal-conteudo] form'); f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})); return 'ok';})()`); await espera(2400);
    let dup = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), erro: (document.querySelector('[data-modal-conteudo] .validation-summary-errors, [data-modal-conteudo] .text-danger')||{}).innerText||document.querySelector('[data-modal-conteudo]').innerText.slice(0,90), email: (document.querySelector('[data-modal-conteudo] input[name=Email]')||{}).value }`);
    check(g + ' (3) e-mail repetido: modal continua aberto', dup.overlay === true, JSON.stringify(dup));
    check(g + ' (3) mensagem do servidor dentro do modal', dup.erro.length > 0, dup.erro.slice(0,60));
    check(g + ' (3) campos preservados', dup.email === 'operador@teste.com', dup.email);

    await fecharEsc(api); await espera(400);
    let fechou = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), foco: document.activeElement && document.activeElement.className }`);
    check(g + ' (4) Esc fecha Nova conta', fechou.overlay === false);
    check(g + ' (4) foco volta ao gatilho', /btn-primary/.test(fechou.foco || ''), fechou.foco);

    await abrirConta(api); await espera(1400);
    await setValor(api, '[data-modal-conteudo] input[name=Nome]', 'Balcao Novo');
    await setValor(api, '[data-modal-conteudo] input[name=Email]', 'balcao.novo.' + tema + '@teste.com');
    await setValor(api, '[data-modal-conteudo] input[name=Senha]', 'Balcao@123');
    await setValor(api, '[data-modal-conteudo] input[name=ConfirmarSenha]', 'Balcao@123');
    await ev(api, `(()=>{const f=document.querySelector('[data-modal-conteudo] form'); f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})); return 'ok';})()`); await espera(2800);
    let criou = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), tem: document.body.innerText.indexOf('Balcao Novo')>=0, aviso: /Conta criada/.test(document.body.innerText) }`);
    check(g + ' (2) conta valida criada e aparece na lista', criou.overlay === false && criou.tem === true, JSON.stringify(criou));
    check(g + ' (2) mensagem de sucesso (TempData) exibida', criou.aviso === true);

    await ev(api, `location.href = ${JSON.stringify(BASE + '/Usuarios/Index')}`); await espera(800);
    const contCriar = criarContadorRede(api, '/Usuarios/Criar');
    await abrirConta(api); await espera(1400);
    await setValor(api, '[data-modal-conteudo] input[name=Nome]', 'Unico Teste');
    await setValor(api, '[data-modal-conteudo] input[name=Email]', 'unico.teste.' + tema + '@teste.com');
    await setValor(api, '[data-modal-conteudo] input[name=Senha]', 'Unico@123');
    await setValor(api, '[data-modal-conteudo] input[name=ConfirmarSenha]', 'Unico@123');
    await ev(api, `(()=>{const b=[...document.querySelectorAll('[data-modal-conteudo] button')].find(x=>/Criar conta/i.test(x.textContent)); if(b){ b.click(); b.click(); } return 'ok';})()`); await espera(2800);
    const cposts = lerContador(contCriar);
    check(g + ' (7) duplo clique cria UMA conta', cposts === 1, 'posts=' + cposts);
}

async function editarContaChecks(api, tema) {
    const g = 'editconta/' + tema;
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Usuarios/Index')}`); await espera(800);
    const gat = await ev(api, `!!document.querySelector('a[data-modal-form][data-modal-titulo="Editar conta"]')`);
    if (!gat) { pular(g + ' Editar conta ainda nao em modal (COMMIT 4 pendente)'); return; }

    // (1) abre o modal da conta do Operador (linha do seed) sem navegar
    const href = await ev(api, `(()=>{ const r=[...document.querySelectorAll('.tabela-responsiva--usuarios tbody tr')].find(x=>/operador@teste\\.com/.test(x.innerText)); const a=r&&r.querySelector('a[data-modal-form][data-modal-titulo="Editar conta"]'); if(!a) return null; a.click(); return a.getAttribute('href'); })()`);
    await espera(1400);
    let st = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), titulo: (document.querySelector('.app-modal__titulo')||{}).textContent, form: !!document.querySelector('[data-modal-conteudo] form'), email: (document.querySelector('[data-modal-conteudo] input[name=Email]')||{}).value, path: location.pathname }`);
    check(g + ' (1) modal Editar conta abre sem navegar', !!href && st.overlay === true && st.form === true && st.path.indexOf('/Usuarios/Index') === 0, JSON.stringify(st));
    check(g + ' (1) titulo "Editar conta"', st.titulo === 'Editar conta', st.titulo);
    check(g + ' (2) abre a conta certa (operador@teste.com)', st.email === 'operador@teste.com', st.email);
    await capturar(api, 'dono', tema, 'conta-editar');

    // (3) validacao vazia: erro do servidor DENTRO do modal, sem navegar
    await setValor(api, '[data-modal-conteudo] input[name=Nome]', '');
    await ev(api, `(()=>{const f=document.querySelector('[data-modal-conteudo] form'); f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})); return 'ok';})()`); await espera(2400);
    let val = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), erro: (document.querySelector('[data-modal-conteudo] .validation-summary-errors, [data-modal-conteudo] .text-danger')||{}).innerText||'', path: location.pathname }`);
    check(g + ' (3) nome vazio: modal continua aberto', val.overlay === true, JSON.stringify(val));
    check(g + ' (3) erro do servidor dentro do modal', val.erro.length > 0, val.erro.slice(0, 60));
    check(g + ' (3) sem navegacao apos erro', val.path.indexOf('/Usuarios/Index') === 0, val.path);

    // (4) Esc fecha e devolve foco ao botao Editar da linha
    await fecharEsc(api); await espera(400);
    let fechou = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), foco: document.activeElement && document.activeElement.className }`);
    check(g + ' (4) Esc fecha', fechou.overlay === false);
    check(g + ' (4) foco volta ao gatilho', /btn-outline-secondary/.test(fechou.foco || ''), fechou.foco);

    // (5) duplo clique no Salvar renomeia UMA vez (contador de rede CDP)
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Usuarios/Index')}`); await espera(800);
    const contEditar = criarContadorRede(api, '/Usuarios/Editar');
    await ev(api, `(()=>{ const r=[...document.querySelectorAll('.tabela-responsiva--usuarios tbody tr')].find(x=>/operador@teste\\.com/.test(x.innerText)); const a=r&&r.querySelector('a[data-modal-form][data-modal-titulo="Editar conta"]'); if(a) a.click(); return 'ok';})()`);
    await espera(1400);
    await setValor(api, '[data-modal-conteudo] input[name=Nome]', 'Operador Teste (QA)');
    await ev(api, `(()=>{const b=[...document.querySelectorAll('[data-modal-conteudo] button')].find(x=>/Salvar/i.test(x.textContent)); if(b){ b.click(); b.click(); } return 'ok';})()`); await espera(2800);
    const eposts = lerContador(contEditar);
    check(g + ' (5) duplo clique Salvar = 1 POST', eposts === 1, 'posts=' + eposts);
    let salvou = await j(api, `{ overlay: !!document.querySelector('.app-modal-overlay'), tem: document.body.innerText.indexOf('Operador Teste (QA)') >= 0, aviso: /Conta atualizada/.test(document.body.innerText) }`);
    check(g + ' (5) renomeou e listagem atualizada', salvou.overlay === false && salvou.tem === true, JSON.stringify(salvou));
    check(g + ' (5) mensagem de sucesso (TempData) exibida', salvou.aviso === true);

    // (6) restaura o nome para nao afetar os demais guards
    await ev(api, `(()=>{ const r=[...document.querySelectorAll('.tabela-responsiva--usuarios tbody tr')].find(x=>/operador@teste\\.com/.test(x.innerText)); const a=r&&r.querySelector('a[data-modal-form][data-modal-titulo="Editar conta"]'); if(a) a.click(); return 'ok';})()`);
    await espera(1400);
    await setValor(api, '[data-modal-conteudo] input[name=Nome]', 'Operador Teste');
    await ev(api, `(()=>{const b=[...document.querySelectorAll('[data-modal-conteudo] button')].find(x=>/Salvar/i.test(x.textContent)); if(b) b.click(); return 'ok';})()`); await espera(2600);
    const restaurado = await ev(api, `document.body.innerText.indexOf('Operador Teste (QA)') < 0`);
    check(g + ' (6) nome restaurado', restaurado === true);

    // (7) a TELA CHEIA continua integra (Ctrl+clique / link direto)
    await ev(api, `location.href = ${JSON.stringify(BASE + href)}`); await espera(1400);
    const cheia = await j(api, `{ h2: (document.querySelector('h2')||{}).textContent||'', form: !!document.querySelector('form[action*="/Usuarios/Editar"]'), path: location.pathname }`);
    check(g + ' (7) tela cheia tem o formulario', cheia.form === true && /Editar conta/.test(cheia.h2), JSON.stringify(cheia));
}


async function operadorChecks(api, tema) {
    const g = 'operador/' + tema;
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index')}`); await espera(800);
    const nEdit = await ev(api, `document.querySelectorAll('a[data-modal-form][data-modal-titulo="Editar produto"]').length`);
    check(g + ' (12) Operador nao ve botao Editar', nEdit === 0, 'n=' + nEdit);
    const rec = await ev(api, `(async()=>{ const r = await fetch('/Home/Editar/1', { credentials:'same-origin', redirect:'manual' }); return { status:r.status, redir: r.url.indexOf('AcessoNegado')>=0 || r.url.indexOf('Login')>=0 }; })()`);
    check(g + ' (12) GET /Home/Editar/1 recusado', rec.status === 0 || rec.status === 403 || rec.redir, JSON.stringify(rec));
    const usuarios = await ev(api, `(async()=>{ const r = await fetch('/Usuarios/Index', { credentials:'same-origin', redirect:'manual' }); return { status:r.status, redir: r.url.indexOf('AcessoNegado')>=0 || r.url.indexOf('Login')>=0 }; })()`);
    check(g + ' (6) Operador nao abre /Usuarios', usuarios.status === 0 || usuarios.status === 403 || usuarios.redir, JSON.stringify(usuarios));
}


async function itensIntactos(api, tema) {
    const g = 'intactos/' + tema;
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index')}`); await espera(700);
    check(g + ' (14) chave de tema presente', (await ev(api, `!!document.querySelector('.app-theme')`)) === true);
    const repor = await ev(api, `document.querySelectorAll('[data-repor]').length`);
    check(g + ' (14) botoes Repor presentes', repor >= 1, 'n=' + repor);
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Caixa/Historico')}`); await espera(700);
    check(g + ' (14) Historico carrega', (await ev(api, `document.body.innerText.length > 50`)) === true);
}

async function ctrlClique(api, tema) {
    const g = 'ctrlclique/' + tema;
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index?busca=ARROZ')}`); await espera(800);
    const r13 = await j(api, `(()=>{const a=document.querySelector('a[data-modal-form]'); if(!a) throw new Error('sem link Editar para ctrl em '+location.pathname); const ns=a.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,ctrlKey:true})); return { naoSequestrado: ns, overlay: !!document.querySelector('.app-modal-overlay') }; })()`);
    check(g + ' (13) Ctrl+clique NAO abre modal', r13.overlay === false, JSON.stringify(r13));
    check(g + ' (13) Ctrl+clique NAO sequestra o link', r13.naoSequestrado === true, JSON.stringify(r13));
    await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Editar/1')}`); await espera(1400);
    const cheia = await j(api, `{ form: !!document.querySelector('.product-form-panel form'), path: location.pathname }`);
    check(g + ' (13) tela cheia tem o formulario', cheia.form === true, JSON.stringify(cheia));
    await ev(api, `(()=>{const f=document.querySelector('.product-form-panel form'); if(f){f.submit(); return 'ok';} return 'sem';})()`); await espera(2200);
    const pos = await ev(api, `location.pathname`);
    check(g + ' (13) salvar na tela cheia redireciona', pos === '/' || pos === '/Home/Index', pos);
}

async function capturar(api, perfil, tema, nome) {
    const shot = await api.cmd('Page.captureScreenshot', {});
    writeFileSync(join(OUT, 'modal-' + perfil + '-' + tema + '-' + nome + '.png'), Buffer.from(shot.data, 'base64'));
}

async function main() {
    mkdirSync(OUT, { recursive: true });
    const ctx = abrirChrome(1440, 960, 9472);
    const api = await conectar(9472);
    await api.cmd('Network.enable', {});
    const [de, ds] = cred.dono;
    try {
        await login(api, BASE, de, ds);
        for (const tema of ['escuro', 'claro']) {
            console.log('=== Dono / tema ' + tema + ' ===');
            await ev(api, `localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            await api.redimensionar(1440, 900);
            await editarChecks(api, tema);
            await ev(api, `location.href = ${JSON.stringify(BASE + '/Home/Index?busca=ARROZ')}`); await espera(700);
            await abrirEditar(api); await espera(1400);
            await capturar(api, 'dono', tema, 'editar');
            await fecharEsc(api); await espera(300);
            await novaContaChecks(api, tema);
            const gatConta = await ev(api, `!!document.querySelector('a[data-modal-form][data-modal-titulo="Nova conta"]')`);
            if (gatConta) {
                await ev(api, `location.href = ${JSON.stringify(BASE + '/Usuarios/Index')}`); await espera(700);
                await abrirConta(api); await espera(1400);
                await capturar(api, 'dono', tema, 'conta');
                await fecharEsc(api); await espera(300);
            }
            await editarContaChecks(api, tema);
            await itensIntactos(api, tema);
            await ctrlClique(api, tema);
        }
        await ev(api, `(() => { const f=document.querySelector('form[action*="Logout"]'); if(f){f.submit(); return 'ok';} return 'x'; })()`);
        await espera(2500);
        const [oe, os] = cred.operador;
        await login(api, BASE, oe, os);
        for (const tema of ['escuro', 'claro']) {
            console.log('=== Operador / tema ' + tema + ' ===');
            await ev(api, `localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            await operadorChecks(api, tema);
        }
    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }
    console.log('');
    console.log('=== modal-check ===');
    console.log('  aprovadas: ' + aprov + ' | falhas: ' + falhas);
    process.exit(falhas > 0 ? 1 : 0);
}
main().catch(e => { console.error('ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });

function fecharEsc(api) { return ev(api, `document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))`); }
