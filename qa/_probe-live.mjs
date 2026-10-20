/*
 * qa/_probe-live.mjs - sonda diagnostica (nao e o check oficial).
 * Mede no browser real as causas-raiz das 31 falhas do relatorio:
 *   1. retangulos/display dos <text> do grafico (falha "altura minima 0.0px")
 *   2. gaveta da sidebar em 390px: rect, estilos computados e toggle real
 *   3. 4 cartoes KPI em 390px: larguras, alturas e overflow de texto
 *   4. rosca de "hoje": vazio real ou bug de filtro
 *   5. periodo "ano" renderiza (fix ParseExact do Index.cshtml)
 * Credenciais chegam por argumento - nunca gravadas aqui.
 * Uso:
 *   node qa/_probe-live.mjs --base http://localhost:5236 --email X --senha Y
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';

const argv = process.argv.slice(2);
function arg(nome, padrao) {
    const i = argv.indexOf(nome);
    return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao;
}
const BASE = arg('--base', 'http://localhost:5236').replace(/\/+$/, '');
const EMAIL = arg('--email', '');
const SENHA = arg('--senha', '');
if (!EMAIL || !SENHA) {
    console.error('probe: informe --email e --senha');
    process.exit(2);
}
const espera = ms => new Promise(r => setTimeout(r, ms));

const ctx = abrirChrome(1440, 1100, 9510);
const api = await conectar(9510);
let falhas = 0;
try {
    await api.cmd('Network.enable'); await api.cmd('Runtime.enable');
    await login(api, BASE, EMAIL, SENHA);
    console.log('login: ok em ' + BASE);

    /* ---- (1) <text> do grafico: 1440px, periodo 30dias ---- */
    await api.redimensionar(1440, 1000); await espera(300);
    await api.ir(BASE + '/Dashboard?periodo=30dias'); await espera(800);
    const texts = await api.avaliar(`(() => {
        const svg = document.querySelector('.dsh-grafico-svg');
        if (!svg) return { erro: 'svg ausente' };
        const lista = Array.from(svg.querySelectorAll('text')).slice(0, 12).map(t => {
            const r = t.getBoundingClientRect();
            const cs = getComputedStyle(t);
            return {
                filho: t.childElementCount, texto: (t.textContent || '').trim().slice(0, 18),
                display: cs.display, w: Math.round(r.width * 10) / 10, h: Math.round(r.height * 10) / 10
            };
        });
        const total = svg.querySelectorAll('text').length;
        const comArea = Array.from(svg.querySelectorAll('text')).filter(t => t.getBoundingClientRect().height > 0).length;
        return { total, comArea, svgRect: svg.getBoundingClientRect().width + 'x' + svg.getBoundingClientRect().height, lista };
    })()`);
    console.log('\n== TEXTS DO GRAFICO ==');
    console.log(JSON.stringify(texts, null, 1));
    if (texts.erro || texts.comArea === 0) { falhas++; console.log('>> texto sem area: causa da falha "0.0px" confirmada'); }

    /* ---- (5) periodo ano renderiza? ---- */
    await api.ir(BASE + '/Dashboard?periodo=ano'); await espera(800);
    const ano = await api.avaliar(`(() => ({
        h1: (document.querySelector('.dsh-saudacao') || {}).textContent || 'AUSENTE',
        svg: !!document.querySelector('.dsh-grafico-svg'),
        erros: (document.body.innerText || '').indexOf('An exception occurred') >= 0,
        rotulos: Array.from(document.querySelectorAll('.dsh-grafico-svg text[font-size="11"]')).map(t => t.textContent.trim()).slice(0, 14)
    }))()`);
    console.log('\n== PERIODO ANO ==');
    console.log(JSON.stringify(ano));
    if (!ano.svg || ano.erros) { falhas++; console.log('>> periodo ano AINDA quebrado'); }

    /* ---- (4) rosca de hoje: vazio real? ---- */
    await api.ir(BASE + '/Dashboard?periodo=hoje'); await espera(800);
    const hoje = await api.avaliar(`(() => ({
        faturamento: ((document.querySelector('.dsh-kpi-card:nth-child(1) .dsh-kpi-valor') || {}).textContent || '').trim(),
        itensRosca: document.querySelectorAll('.dsh-rosca-item').length,
        temVazio: !!document.querySelector('.dsh-rosca-wrapper .dsh-vazio'),
        textoVazio: ((document.querySelector('.dsh-rosca-wrapper .dsh-vazio') || {}).textContent || '').trim(),
        soma: Array.from(document.querySelectorAll('.dsh-rosca-pct')).reduce((a, e) => a + parseFloat(e.textContent.replace('%', '').replace(',', '.')), 0)
    }))()`);
    console.log('\n== ROSCA HOJE ==');
    console.log(JSON.stringify(hoje));

    /* ---- (2)(3) MOBILE 390px: gaveta, toggle e cartoes ---- */
    await api.redimensionar(390, 844); await espera(400);
    await api.ir(BASE + '/Dashboard?periodo=30dias'); await espera(800);
    const gaveta = await api.avaliar(`(() => {
        const s = document.querySelector('.app-sidebar');
        const cs = s ? getComputedStyle(s) : null;
        const r = s ? s.getBoundingClientRect() : null;
        const fora = [];
        document.querySelectorAll('*').forEach(el => {
            const b = el.getBoundingClientRect();
            if (b.width > 0 && b.height > 0 && (b.right > window.innerWidth + 1 || b.left < -1)) {
                fora.push(el.tagName + '.' + String(el.className).slice(0, 40) + ' [' + Math.round(b.left) + ',' + Math.round(b.right) + ']');
            }
        });
        return {
            dataSidebar: document.documentElement.getAttribute('data-sidebar'),
            classe: s ? s.className : 'SEM SIDEBAR',
            rect: r ? (Math.round(r.left) + '..' + Math.round(r.right)) : '-',
            position: cs && cs.position, left: cs && cs.left, transform: cs && cs.transform,
            visibility: cs && cs.visibility, display: cs && cs.display, zIndex: cs && cs.zIndex,
            hambExp: document.querySelector('.app-hamburguer') ? document.querySelector('.app-hamburguer').getAttribute('aria-expanded') : 'sem hamb',
            fora: fora.slice(0, 10), foraTotal: fora.length
        };
    })()`);
    console.log('\n== GAVETA 390px (fechada) ==');
    console.log(JSON.stringify(gaveta, null, 1));
    if (gaveta.foraTotal > 0) { falhas++; console.log('>> elemento fora da viewport em 390px'); }

    // toggle real: abrir, medir, fechar, medir
    const toggle = await api.avaliar(`(() => {
        if (!window.SHELL) return { erro: 'SHELL ausente' };
        window.SHELL.alternar();
        const s = document.querySelector('.app-sidebar');
        const aberto = s.classList.contains('aberto');
        const r1 = s.getBoundingClientRect();
        window.SHELL.alternar();
        const r2 = s.getBoundingClientRect();
        const cort = document.querySelector('.app-cortina');
        return { abriu: aberto, rectAberto: Math.round(r1.left) + '..' + Math.round(r1.right),
                 rectFechado: Math.round(r2.left) + '..' + Math.round(r2.right),
                 cortina: cort ? cort.classList.contains('ativo') : 'sem cortina' };
    })()`);
    console.log('\n== TOGGLE GAVETA ==');
    console.log(JSON.stringify(toggle));

    const cartoes = await api.avaliar(`(() => {
        const cards = Array.from(document.querySelectorAll('.dsh-kpi-card'));
        return cards.map(c => {
            const r = c.getBoundingClientRect();
            const v = c.querySelector('.dsh-kpi-valor');
            return { w: Math.round(r.width), h: Math.round(r.height * 10) / 10,
                     valorOverflow: v ? (v.scrollWidth - v.clientWidth) : 'sem valor',
                     valor: v ? v.textContent.trim().slice(0, 24) : '' };
        });
    })()`);
    console.log('\n== CARTOES KPI 390px ==');
    console.log(JSON.stringify(cartoes, null, 1));
    const alturas = cartoes.map(c => c.h);
    if (alturas.length === 4 && Math.max(...alturas) - Math.min(...alturas) > 1) {
        falhas++; console.log('>> cartoes com alturas diferentes em 390px');
    }
    if (cartoes.some(c => typeof c.valorOverflow === 'number' && c.valorOverflow > 1)) {
        falhas++; console.log('>> valor do cartao transborda em 390px');
    }

    /* ---- (6) dump extra: HTML do svg (30dias) + status/erro do ano ---- */
    const dump = await api.avaliar(`(async () => {
        const svg = document.querySelector('.dsh-grafico-svg');
        const resp = await fetch('/Dashboard?periodo=ano', { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        const corpo = await resp.text();
        const idxFmt = corpo.indexOf('FormatException');
        return {
            svgHTML: svg ? svg.outerHTML.slice(0, 1200) : 'sem svg',
            statusAno: resp.status,
            trechoErro: idxFmt >= 0 ? corpo.slice(Math.max(0, idxFmt - 300), idxFmt + 300) : 'sem FormatException',
            temSaudacaoAno: corpo.indexOf('dsh-saudacao') >= 0
        };
    })()`);
    console.log('\n== DUMP SVG (30dias) ==');
    console.log(dump.svgHTML);
    console.log('\n== ANO via fetch ==');
    console.log('status: ' + dump.statusAno + ' | tem dsh-saudacao: ' + dump.temSaudacaoAno);
    console.log(dump.trechoErro.slice(0, 700));

    /* ---- (7) excecao real do periodo ano (pagina de erro do dev) ---- */
    const erroAno = await api.avaliar(`(async () => {
        const r = await fetch('/Dashboard?periodo=ano');
        const t = await r.text();
        const i = t.indexOf('unhandled exception');
        const j = t.indexOf('Exception:');
        const k = Math.max(i, j);
        return { status: r.status, trecho: k >= 0 ? t.slice(k, k + 900) : t.slice(0, 400) };
    })()`);
    console.log('\n== ERRO ANO (detalhe) ==');
    console.log('status ' + erroAno.status);
    console.log(erroAno.trecho.replace(/\\s+/g, ' ').slice(0, 800));

    /* ---- (8) periodo seletor e tabela responsiva: overflow intencional? ---- */
    await api.ir(BASE + '/Dashboard?periodo=hoje'); await espera(500);
    const contenedores = await api.avaliar(`(() => {
        function info(sel) {
            const el = document.querySelector(sel);
            if (!el) return sel + ': AUSENTE';
            const cs = getComputedStyle(el);
            return sel + ': overflowX=' + cs.overflowX + ' wrap=' + cs.flexWrap + ' scrollW=' + el.scrollWidth + ' clientW=' + el.clientWidth;
        }
        return [info('.dsh-periodo-seletor'), info('.dsh-tabela-container'), info('.dsh-tabela-responsiva'), info('.dsh-container')].join(' | ');
    })()`);
    console.log('\n== CONTAINERS ==');
    console.log(contenedores);

    console.log('\n=== probe: ' + (falhas === 0 ? 'TUDO OK' : falhas + ' problema(s) confirmado(s)') + ' ===');
} finally {
    fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
}
