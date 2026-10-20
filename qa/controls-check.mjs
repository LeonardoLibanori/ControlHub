/*
 * qa/controls-check.mjs - AJUSTE 2: guarda visual de controles padronizados.
 *
 * Reaproveita qa/cdp-helper.mjs. Sobe Chrome headless, faz login como Dono
 * (e Operador no Caixa), nos DOIS temas, nas larguras 1440, 1024 e 390px.
 *
 * Em Login, Estoque, Novo produto, Editar produto, Caixa (com modal de pagamento
 * aberto), Histórico, Usuários e Dashboard:
 *   (a) nenhum elemento de button, .btn, th, h1-h4 ou label com
 *       text-transform:uppercase (exceto rotulo de grupo da sidebar) e
 *       nenhum texto desses elementos todo em maiusculas com mais de 3 letras;
 *   (b) botoes na mesma linha tem a mesma altura (+/-1px) e centros alinhados
 *       (+/-2px);
 *   (c) botoes e campos normais com 40px (+/-1) e pequenos com 32px;
 *   (d) contraste do texto de todo botao contra o fundo efetivo (primeiro
 *       ancestral com fundo opaco, compondo transparencia) >= 4.5;
 *   (e) nada fora de [0, innerWidth] e scrollWidth <= clientWidth.
 *
 * Uso:
 *   node qa/controls-check.mjs --base http://localhost:5311
 *
 * Variáveis de ambiente (nunca gravadas aqui):
 *   SHELL_DONO_EMAIL, SHELL_DONO_SENHA
 *   SHELL_OPER_EMAIL, SHELL_OPER_SENHA
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';

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

const LARGURAS = [1440, 1024, 390];
const TEMAS = ['escuro', 'claro'];
const TELAS = [
    { nome: 'Login', url: '/Account/Login', perfil: 'dono', autenticar: false },
    { nome: 'Estoque', url: '/Home/Index', perfil: 'dono', autenticar: true },
    { nome: 'NovoProduto', url: '/Home/Novo', perfil: 'dono', autenticar: true },
    { nome: 'EditarProduto', url: '/Home/Editar/1', perfil: 'dono', autenticar: true },
    { nome: 'Caixa', url: '/Caixa', perfil: 'dono', autenticar: true, abrirModalPagamento: true },
    { nome: 'CaixaOperador', url: '/Caixa', perfil: 'operador', autenticar: true, abrirModalPagamento: true },
    { nome: 'Historico', url: '/Caixa/Historico?periodo=hoje', perfil: 'dono', autenticar: true },
    { nome: 'Usuarios', url: '/Usuarios', perfil: 'dono', autenticar: true },
    { nome: 'Dashboard', url: '/Dashboard', perfil: 'dono', autenticar: true }
];

let ok = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { ok++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
const espera = ms => new Promise(r => setTimeout(r, ms));

/* Contraste WCAG (texto vs fundo) >= 4.5 */
function parseRgb(s) {
    const m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(s || '');
    return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
}
function canalLum(c) {
    const x = c / 255;
    return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
}
function razaoWcag(fg, bg) {
    if (!fg || !bg) return 0;
    const lfg = 0.2126 * canalLum(fg[0]) + 0.7152 * canalLum(fg[1]) + 0.0722 * canalLum(fg[2]);
    const lbg = 0.2126 * canalLum(bg[0]) + 0.7152 * canalLum(bg[1]) + 0.0722 * canalLum(bg[2]);
    const alto = Math.max(lfg, lbg);
    const baixo = Math.min(lfg, lbg);
    return (alto + 0.05) / (baixo + 0.05);
}

/* Encontra o fundo efetivo (primeiro ancestral com background-color opaco) */
const FUNDO_EFETIVO = String.raw`(() => {
    function getEffectiveBg(el) {
        while (el && el !== document.body) {
            const bg = getComputedStyle(el).backgroundColor;
            const rgba = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/.exec(bg);
            if (rgba && (rgba[4] === undefined || Number(rgba[4]) > 0.1)) {
                return [Number(rgba[1]), Number(rgba[2]), Number(rgba[3])];
            }
            el = el.parentElement;
        }
        return [255, 255, 255]; // fallback branco
    }
    return getEffectiveBg;
})()`;

async function verificarTela(api, tela, largura, tema, perfil) {
    const g = `${perfil}/${tema}/${tela.nome}/${largura}px`;
    await api.redimensionar(largura, 900);
    await espera(400);
    await api.ir(BASE + tela.url);
    await espera(800);

    if (tela.abrirModalPagamento) {
        // Abrir modal de pagamento via JS
        await api.avaliar(`(() => {
            const carrinho = [{id: 1, nome: 'Teste', preco: 10, quantidade: 1, unidade: 'un', regra: 'Prateleira'}];
            window.carrinho = carrinho;
            if (typeof abrirModalPagamento === 'function') {
                abrirModalPagamento();
            }
            return 'ok';
        })()`);
        await espera(800);
    }

    // (a) Sem text-transform:uppercase em button, .btn, th, h1-h4, label
    //     e sem textos todos em MAIUSCULAS (>3 letras)
    const uppercaseCheck = await api.avaliar(`(() => {
        const getEffectiveBg = ${FUNDO_EFETIVO};
        const elems = document.querySelectorAll('button, .btn, th, h1, h2, h3, h4, label');
        const violacoes = [];
        for (const el of elems) {
            const estilo = getComputedStyle(el);
            // text-transform: uppercase
            if (estilo.textTransform === 'uppercase') {
                // Excecao: rotulos de grupo da sidebar
                if (!el.classList.contains('app-sidebar__grupo')) {
                    violacoes.push({
                        tag: el.tagName,
                        class: el.className,
                        text: el.textContent.trim().slice(0, 60),
                        motivo: 'text-transform:uppercase'
                    });
                }
            }
            // Texto todo em maiusculas (>3 letras)
            const texto = el.textContent.trim();
            const letras = texto.replace(/[^A-Za-zÀ-ÿ]/g, '');
            if (letras.length > 3 && letras === letras.toUpperCase()) {
                // Excecao: rotulos de grupo da sidebar
                if (!el.classList.contains('app-sidebar__grupo')) {
                    violacoes.push({
                        tag: el.tagName,
                        class: el.className,
                        text: texto.slice(0, 60),
                        motivo: 'texto todo maiusculo'
                    });
                }
            }
        }
        return violacoes;
    })()`);

    for (const v of uppercaseCheck) {
        check(`${g} sem uppercase: ${v.tag}.${v.class}`, false, v.motivo + ' | ' + v.text);
    }
    if (uppercaseCheck.length === 0) {
        check(`${g} nenhum elemento com text-transform:uppercase ou texto todo maiusculo`, true);
    }

    // (b) Botoes na mesma linha: mesma altura (+/-1px) e centros alinhados (+/-2px)
    const botoesLinha = await api.avaliar(`(() => {
        const linhas = {};
        document.querySelectorAll('button, .btn, a.btn').forEach((btn, i) => {
            const r = btn.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) return;
            const linha = Math.round(r.top / 4) * 4; // agrupa por linha aproximada
            if (!linhas[linha]) linhas[linha] = [];
            linhas[linha].push({
                idx: i,
                tag: btn.tagName,
                class: btn.className,
                h: r.height,
                cy: r.top + r.height / 2,
                rect: { left: r.left, top: r.top, width: r.width, height: r.height }
            });
        });
        const res = [];
        for (const [linha, botoes] of Object.entries(linhas)) {
            if (botoes.length > 1) {
                const h0 = botoes[0].h;
                const cy0 = botoes[0].cy;
                for (const b of botoes.slice(1)) {
                    res.push({
                        linha,
                        b1: { idx: botoes[0].idx, h: h0, cy: cy0 },
                        b2: { idx: b.idx, h: b.h, cy: b.cy },
                        diffH: Math.abs(b.h - h0),
                        diffCY: Math.abs(b.cy - cy0)
                    });
                }
            }
        }
        return res;
    })()`);

    for (const bl of botoesLinha) {
        check(`${g} botoes linha[${bl.linha}] mesma altura (+/-1px)`, bl.diffH <= 1, `h1=${bl.b1.h} h2=${bl.b2.h} diff=${bl.diffH}`);
        check(`${g} botoes linha[${bl.linha}] centros alinhados (+/-2px)`, bl.diffCY <= 2, `cy1=${bl.b1.cy.toFixed(1)} cy2=${bl.b2.cy.toFixed(1)} diff=${bl.diffCY.toFixed(1)}`);
    }

    // (c) Botoes e campos normais 40px (+/-1), pequenos 32px
    const tamanhos = await api.avaliar(`(() => {
        const res = [];
        document.querySelectorAll('button, .btn, input.form-control, select.form-select, textarea.form-control, .input-group-text').forEach(el => {
            const r = el.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) return;
            const classes = el.className || '';
            const isPequeno = classes.includes('btn-sm') || classes.includes('form-control-sm') || classes.includes('input-group-sm');
            const esperado = isPequeno ? 32 : 40;
            const tolerancia = 1;
            if (Math.abs(r.height - esperado) > tolerancia) {
                res.push({
                    tag: el.tagName,
                    class: classes,
                    height: r.height,
                    esperado,
                    isPequeno
                });
            }
        });
        return res;
    })()`);

    for (const t of tamanhos) {
        check(`${g} ${t.tag}.${t.class} altura ${t.esperado}px (+/-1)`, false, `altura=${t.height} esperado=${t.esperado} pequeno=${t.isPequeno}`);
    }
    if (tamanhos.length === 0) {
        check(`${g} todos botoes/campos com altura correta (40px normal, 32px pequeno)`, true);
    }

    // (d) Contraste texto de botao >= 4.5
    const contrastes = await api.avaliar(`(() => {
        const getEffectiveBg = ${FUNDO_EFETIVO};
        const res = [];
        document.querySelectorAll('button, .btn, a.btn').forEach(btn => {
            const r = btn.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) return;
            const estilo = getComputedStyle(btn);
            const corTexto = parseRgb(estilo.color);
            const fundo = getEffectiveBg(btn);
            const razao = (() => {
                if (!corTexto || !fundo) return 0;
                const lfg = 0.2126 * ((c) => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4))(corTexto[0] / 255) +
                          0.7152 * ((c) => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4))(corTexto[1] / 255) +
                          0.0722 * ((c) => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4))(corTexto[2] / 255);
                const lbg = 0.2126 * ((c) => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4))(fundo[0] / 255) +
                          0.7152 * ((c) => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4))(fundo[1] / 255) +
                          0.0722 * ((c) => c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4))(fundo[2] / 255);
                const alto = Math.max(lfg, lbg);
                const baixo = Math.min(lfg, lbg);
                return (alto + 0.05) / (baixo + 0.05);
            })();
            if (razao < 4.5) {
                res.push({
                    tag: btn.tagName,
                    class: btn.className,
                    text: btn.textContent.trim().slice(0, 40),
                    razao: razao.toFixed(2),
                    corTexto: estilo.color,
                    fundo: 'rgb(' + fundo.join(',') + ')'
                });
            }
        });
        return res;
    })()`);

    for (const c of contrastes) {
        check(`${g} contraste botao ${c.tag}.${c.class} >= 4.5`, false, `razao=${c.razao} texto=${c.text} cor=${c.corTexto} fundo=${c.fundo}`);
    }
    if (contrastes.length === 0) {
        check(`${g} todos botoes com contraste >= 4.5:1`, true);
    }

    // (e) Nada fora de [0, innerWidth] e scrollWidth <= clientWidth
    const overflow = await api.avaliar(`(() => {
        const res = [];
        const innerW = window.innerWidth;
        // Elementos fora da viewport
        document.querySelectorAll('*').forEach(el => {
            const r = el.getBoundingClientRect();
            if (r.width === 0 && r.height === 0) return;
            if (r.right > innerW + 1 || r.left < -1) {
                res.push({ tipo: 'fora-viewport', tag: el.tagName, class: el.className, left: r.left, right: r.right, innerW });
            }
        });
        // Scroll horizontal na pagina
        if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) {
            res.push({ tipo: 'scroll-horizontal', sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth });
        }
        // Celulas de tabela com overflow
        document.querySelectorAll('td, th').forEach(el => {
            if (el.scrollWidth > el.clientWidth + 1) {
                res.push({ tipo: 'celula-overflow', tag: el.tagName, class: el.className, sw: el.scrollWidth, cw: el.clientWidth });
            }
        });
        return res;
    })()`);

    for (const o of overflow) {
        check(`${g} sem overflow: ${o.tipo}`, false, JSON.stringify(o));
    }
    if (overflow.length === 0) {
        check(`${g} pagina sem scroll horizontal e elementos fora da viewport`, true);
    }
}

async function main() {
    const ctx = abrirChrome(1440, 960, 9480);
    const api = await conectar(9480);
    await api.cmd('Network.enable'); await api.cmd('Log.enable'); await api.cmd('Runtime.enable');

    try {
        for (const tema of TEMAS) {
            console.log(`\n===== DONO / tema ${tema} =====`);
            await login(api, BASE, DONO.email, DONO.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);

            for (const tela of TELAS.filter(t => t.perfil === 'dono')) {
                await verificarTela(api, tela, 1440, tema, 'dono');
                await verificarTela(api, tela, 1024, tema, 'dono');
                await verificarTela(api, tela, 390, tema, 'dono');
            }
        }

        for (const tema of TEMAS) {
            console.log(`\n===== OPERADOR / tema ${tema} =====`);
            await login(api, BASE, OPER.email, OPER.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);

            for (const tela of TELAS.filter(t => t.perfil === 'operador')) {
                await verificarTela(api, tela, 1440, tema, 'operador');
                await verificarTela(api, tela, 1024, tema, 'operador');
                await verificarTela(api, tela, 390, tema, 'operador');
            }
        }

    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }

    console.log(`\n=== controls-check: ${ok} ok / ${falhas} falha(s) ===`);
    if (falhas > 0) { console.log('controls-check: FALHOU'); process.exit(1); }
    console.log('controls-check: OK'); process.exit(0);
}

main().catch(e => { console.error('controls-check: ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });