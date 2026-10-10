/*
 * qa/layout-check.mjs - AJUSTE 1: guarda visual das tabelas responsivas.
 *
 * Reaproveita qa/cdp-helper.mjs. Sobe Chrome headless, faz login como Dono
 * e Operador, nos DOIS temas, nas larguras 1440, 1024, 768 e 390px.
 *
 * Seed pela tela "Novo produto" no .db temporário:
 *   - nome normal
 *   - 40 caracteres com espaços
 *   - 120 caracteres sem espaço (ou máximo aceito)
 *   - nome com emoji
 *
 * Em Estoque, Histórico e Usuários, verifica:
 * (1) scrollWidth <= clientWidth da página (sem scroll horizontal na página)
 * (2) todo botão visível de tabela tem rect dentro de [0, innerWidth] e
 *     elementFromPoint no centro devolve o próprio botão
 * (3) em cada linha do Estoque, Editar e Excluir têm a mesma altura (+/-1px)
 *     e o mesmo centro vertical (+/-2px)
 * (4) o nome longo aparece inteiro e nenhuma célula tem scrollWidth > clientWidth
 *     em >= 768px
 * (5) Operador: Estoque sem Novo produto, Exportar, Editar e Excluir
 *
 * Uso:
 *   node qa/layout-check.mjs --base http://localhost:5311
 *
 * Variáveis de ambiente (nunca gravadas aqui):
 *   SHELL_DONO_EMAIL, SHELL_DONO_SENHA
 *   SHELL_OPER_EMAIL, SHELL_OPER_SENHA
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
import { execFileSync } from 'node:child_process';

const argv = process.argv.slice(2);
function arg(nome, padrao) {
    const i = argv.indexOf(nome);
    return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao;
}
const BASE = arg('--base', process.env.SHELL_BASE || 'http://localhost:5311').replace(/\/+$/, '');

const DONO = {
    email: process.env.SHELL_DONO_EMAIL || 'dono@teste.com',
    senha: process.env.SHELL_DONO_SENHA || '***REMOVIDO***'
};
const OPER = {
    email: process.env.SHELL_OPER_EMAIL || 'operador@teste.com',
    senha: process.env.SHELL_OPER_SENHA || 'Operador@123'
};

const LARGURAS = [1440, 1024, 768, 390];
const TEMAS = ['escuro', 'claro'];
const TABELAS = [
    { nome: 'Estoque', url: '/Home/Index', seletor: '.tabela-responsiva--estoque' },
    { nome: 'Historico', url: '/Caixa/Historico?periodo=hoje', seletor: '.tabela-responsiva--historico' },
    { nome: 'Usuarios', url: '/Usuarios', seletor: '.tabela-responsiva--usuarios' }
];

const NOMES_TESTE = [
    { tipo: 'normal', valor: 'Produto Normal' },
    { tipo: '40_com_espacos', valor: 'Produto com Quarenta Caracteres Com Espacos' },
    { tipo: '120_sem_espaco', valor: 'X'.repeat(120) },
    { tipo: 'emoji', valor: 'Produto 🎉🚀✨ com Emojis 😀🎯' }
];

let ok = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { ok++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
const espera = ms => new Promise(r => setTimeout(r, ms));

/* Cria produto via POST na tela Novo (usa o formulário real) */
async function criarProduto(api, nome) {
    await api.ir(BASE + '/Home/Novo');
    await espera(800);
    return await api.avaliar(`(() => {
        const f = document.querySelector('form');
        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        const n = f.querySelector('input[name="Nome"]');
        const t = f.querySelector('select[name="Tipo"]');
        const q = f.querySelector('input[name="Quantidade"]');
        const p = f.querySelector('input[name="Preco"]');
        const c = f.querySelector('input[name="CodigoBarras"]');
        set.call(n, ${JSON.stringify(nome)}); n.dispatchEvent(new Event('input', { bubbles: true }));
        t.value = 'Unidade';
        set.call(q, '10'); q.dispatchEvent(new Event('input', { bubbles: true }));
        set.call(p, '9.99'); p.dispatchEvent(new Event('input', { bubbles: true }));
        set.call(c, 'TESTE' + Date.now()); c.dispatchEvent(new Event('input', { bubbles: true }));
        f.submit(); return 'ok';
    })()`);
}

/* Verifica uma tabela específica */
async function verificarTabela(api, tabela, largura, tema, perfil) {
    const g = `${perfil}/${tema}/${tabela.nome}/${largura}px`;
    await api.redimensionar(largura, 900);
    await espera(400);
    await api.ir(BASE + tabela.url);
    await espera(800);

    // (1) Página não tem scroll horizontal
    const pageScroll = await api.avaliar(`document.documentElement.scrollWidth <= document.documentElement.clientWidth`);
    check(`${g} pagina sem scroll horizontal`, pageScroll, 'scrollWidth=' + await api.avaliar('document.documentElement.scrollWidth') + ' clientWidth=' + await api.avaliar('document.documentElement.clientWidth'));

    // Encontrar a tabela
    const tblInfo = await api.avaliar(`(() => {
        const t = document.querySelector(${JSON.stringify(tabela.seletor)});
        if (!t) return { existe: false };
        const rect = t.getBoundingClientRect();
        const container = t.closest('.tabela-card, .table-responsive, .dash-rolagem');
        const cRect = container ? container.getBoundingClientRect() : null;
        return {
            existe: true,
            tabela: { width: rect.width, left: rect.left, right: rect.right },
            container: cRect ? { width: cRect.width, left: cRect.left, right: cRect.right } : null,
            linhas: t.querySelectorAll('tbody tr').length,
            ths: Array.from(t.querySelectorAll('thead th')).map(th => ({ text: th.textContent.trim(), width: th.getBoundingClientRect().width })),
            colunas: t.querySelectorAll('thead th').length
        };
    })()`);

    if (!tblInfo.existe) {
        check(`${g} tabela ${tabela.nome} existe`, false, 'seletor nao encontrado');
        return;
    }

    check(`${g} tabela tem linhas`, tblInfo.linhas > 0, 'linhas=' + tblInfo.linhas);

    // (2) Botões visíveis dentro da viewport
    const botoesInfo = await api.avaliar(`(() => {
        const t = document.querySelector(${JSON.stringify(tabela.seletor)});
        if (!t) return [];
        const botoes = Array.from(t.querySelectorAll('tbody button, tbody a.btn-editar, tbody a.btn-excluir, tbody .history-action'));
        return botoes.map((b, i) => {
            const r = b.getBoundingClientRect();
            const centroX = r.left + r.width / 2;
            const centroY = r.top + r.height / 2;
            const el = document.elementFromPoint(centroX, centroY);
            return {
                idx: i,
                tag: b.tagName,
                class: b.className,
                rect: { left: r.left, top: r.top, width: r.width, height: r.height, right: r.right, bottom: r.bottom },
                dentroViewport: r.left >= 0 && r.right <= window.innerWidth && r.top >= 0 && r.bottom <= window.innerHeight,
                elementFromPoint: el === b ? 'self' : (el ? el.tagName + '.' + el.className : 'null')
            };
        });
    })()`);

    for (const btn of botoesInfo) {
        check(`${g} botao[${btn.idx}] ${btn.tag}.${btn.class} dentro da viewport`, btn.dentroViewport, JSON.stringify(btn.rect));
        check(`${g} botao[${btn.idx}] elementFromPoint retorna o proprio botao`, btn.elementFromPoint === 'self', btn.elementFromPoint);
    }

    // (3) Para Estoque: Editar e Excluir mesma altura e centro vertical
    if (tabela.nome === 'Estoque') {
        const alturas = await api.avaliar(`(() => {
            const t = document.querySelector(${JSON.stringify(tabela.seletor)});
            if (!t) return [];
            const res = [];
            t.querySelectorAll('tbody tr').forEach(tr => {
                const editar = tr.querySelector('a.btn-editar');
                const excluir = tr.querySelector('button.btn-excluir, form button.btn-excluir');
                if (editar && excluir) {
                    const re = editar.getBoundingClientRect();
                    const rx = excluir.getBoundingClientRect();
                    res.push({
                        editar: { h: re.height, cy: re.top + re.height / 2 },
                        excluir: { h: rx.height, cy: rx.top + rx.height / 2 },
                        diffH: Math.abs(re.height - rx.height),
                        diffCY: Math.abs((re.top + re.height / 2) - (rx.top + rx.height / 2))
                    });
                }
            });
            return res;
        })()`);

        for (let i = 0; i < alturas.length; i++) {
            const a = alturas[i];
            check(`${g} linha[${i}] Editar/Excluir mesma altura (+/-1px)`, a.diffH <= 1, `editarH=${a.editar.h} excluirH=${a.excluir.h} diff=${a.diffH}`);
            check(`${g} linha[${i}] Editar/Excluir mesmo centro vertical (+/-2px)`, a.diffCY <= 2, `editarCY=${a.editar.cy.toFixed(1)} excluirCY=${a.excluir.cy.toFixed(1)} diff=${a.diffCY.toFixed(1)}`);
        }
    }

    // (4) Em >= 768px: nome longo aparece inteiro, nenhuma célula tem scrollWidth > clientWidth
    if (largura >= 768) {
        const celulas = await api.avaliar(`(() => {
            const t = document.querySelector(${JSON.stringify(tabela.seletor)});
            if (!t) return [];
            const res = [];
            t.querySelectorAll('tbody td').forEach((td, i) => {
                if (td.scrollWidth > td.clientWidth + 1) { // +1 tolerância arredondamento
                    res.push({ idx: i, sw: td.scrollWidth, cw: td.clientWidth, text: td.textContent.trim().slice(0, 60) });
                }
            });
            return res;
        })()`);

        for (const c of celulas) {
            check(`${g} celula[${c.idx}] sem overflow (scrollWidth <= clientWidth)`, false, `sw=${c.sw} cw=${c.cw} text=${c.text}`);
        }
        if (celulas.length === 0) {
            check(`${g} todas as celulas sem overflow interno`, true);
        }
    }

    // Verificar se nome longo aparece inteiro (sem reticências/troncação)
    const nomesCompletos = await api.avaliar(`(() => {
        const t = document.querySelector(${JSON.stringify(tabela.seletor)});
        if (!t) return [];
        const res = [];
        t.querySelectorAll('tbody td:first-child a, tbody td:first-child').forEach((cel, i) => {
            const texto = cel.textContent.trim();
            // Verifica se tem reticências ou foi cortado visualmente
            const estilo = getComputedStyle(cel);
            const temEllipsis = estilo.textOverflow === 'ellipsis';
            const temOverflow = estilo.overflow === 'hidden' && estilo.whiteSpace === 'nowrap';
            res.push({ idx: i, texto: texto.slice(0, 80), temEllipsis, temOverflow, textOverflow: estilo.textOverflow, overflow: estilo.overflow, whiteSpace: estilo.whiteSpace });
        });
        return res;
    })()`);

    for (const n of nomesCompletos) {
        check(`${g} nome[${n.idx}] sem truncamento visual (ellipsis/overflow:hidden+nowrap)`, !n.temEllipsis && !n.temOverflow, `ellipsis=${n.temEllipsis} overflow=${n.temOverflow} textOverflow=${n.textOverflow} whiteSpace=${n.whiteSpace}`);
    }
}

/* Verifica permissões do Operador */
async function verificarOperadorPermissoes(api, largura, tema) {
    const g = `operador/${tema}/${largura}px`;
    await api.redimensionar(largura, 900);
    await espera(400);
    await api.ir(BASE + '/Home/Index');
    await espera(800);

    // Operador NÃO deve ver: Novo produto, Exportar Excel, Editar, Excluir
    const botoesVisiveis = await api.avaliar(`(() => {
        const res = {
            novoProduto: !!document.querySelector('a[href*="/Home/Novo"]'),
            exportarExcel: !!document.querySelector('a[href*="ExportarEstoqueExcel"]'),
            editar: document.querySelectorAll('a.btn-editar, a[href*="/Home/Editar"]').length,
            excluir: document.querySelectorAll('button.btn-excluir, form[action*="/Home/Excluir"] button').length
        };
        return res;
    })()`);

    check(`${g} Operador NAO ve Novo produto`, !botoesVisiveis.novoProduto, 'visivel=' + botoesVisiveis.novoProduto);
    check(`${g} Operador NAO ve Exportar Excel`, !botoesVisiveis.exportarExcel, 'visivel=' + botoesVisiveis.exportarExcel);
    check(`${g} Operador NAO ve botoes Editar`, botoesVisiveis.editar === 0, 'qtd=' + botoesVisiveis.editar);
    check(`${g} Operador NAO ve botoes Excluir`, botoesVisiveis.excluir === 0, 'qtd=' + botoesVisiveis.excluir);
}

async function main() {
    const ctx = abrirChrome(1440, 960, 9470);
    const api = await conectar(9470);
    await api.cmd('Network.enable'); await api.cmd('Log.enable'); await api.cmd('Runtime.enable');

    try {
        // Seed: criar produtos de teste como Dono
        console.log('\n=== SEED: Criando produtos de teste ===');
        await login(api, BASE, DONO.email, DONO.senha);
        await api.avaliar(`localStorage.setItem('controlhub-tema', 'escuro'); 'ok'`);
        for (const nt of NOMES_TESTE) {
            await criarProduto(api, nt.valor);
            await espera(500);
        }

        // Testes Dono
        for (const tema of TEMAS) {
            console.log(`\n===== DONO / tema ${tema} =====`);
            await login(api, BASE, DONO.email, DONO.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            for (const largura of LARGURAS) {
                for (const tabela of TABELAS) {
                    await verificarTabela(api, tabela, largura, tema, 'dono');
                }
            }
        }

        // Testes Operador
        for (const tema of TEMAS) {
            console.log(`\n===== OPERADOR / tema ${tema} =====`);
            await login(api, BASE, OPER.email, OPER.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            for (const largura of LARGURAS) {
                for (const tabela of TABELAS) {
                    await verificarTabela(api, tabela, largura, tema, 'operador');
                }
                await verificarOperadorPermissoes(api, largura, tema);
            }
        }

    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }

    console.log(`\n=== layout-check: ${ok} ok / ${falhas} falha(s) ===`);
    if (falhas > 0) { console.log('layout-check: FALHOU'); process.exit(1); }
    console.log('layout-check: OK'); process.exit(0);
}

main().catch(e => { console.error('layout-check: ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });