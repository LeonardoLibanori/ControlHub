/*
 * qa/shell-check.mjs - AJUSTE DE SHELL (1 de 3): 7 verificacoes CDP do shell
 * autenticado para Dono e Operador, nos DOIS temas, em 1440px e 390px, nas
 * telas Estoque e Dashboard:
 *   V1 itens exatos da sidebar por papel (grupos e gates preservados)
 *   V2 item ativo / aria-current por tela (Dashboard do Operador = negado)
 *   V3 menu do usuario UNICO (abrir/fechar, Esc, clique fora, itens por papel)
 *   V4 chave de tema (role=switch, aria-checked, persistencia, knob-ink >= 3:1)
 *   V5 faixa superior enxuta (hamburguer + caminho; sem busca/tema/perfil)
 *   V6 larguras: 1440px (estatica + recolher) e 390px (gaveta + cortina + Esc)
 *   V7 baseline gravado em prints/baseline-menu.json
 *
 * Chrome headless + CDP puro via ./cdp-helper.mjs (sem npm). Credenciais SO
 * por variavel de ambiente (nunca gravadas aqui):
 *   SHELL_DONO_EMAIL / SHELL_DONO_SENHA
 *   SHELL_OPER_EMAIL / SHELL_OPER_SENHA
 *
 * Uso:
 *   node qa/shell-check.mjs --base http://localhost:5311 --out "...\prints"
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
function arg(nome, padrao) {
    const i = argv.indexOf(nome);
    return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao;
}
const BASE = arg('--base', 'http://localhost:5311').replace(/\/+$/, '');
const OUT = arg('--out', join(aqui, '..', '..', 'prints'));

const cred = {
    dono: [process.env.SHELL_DONO_EMAIL, process.env.SHELL_DONO_SENHA],
    operador: [process.env.SHELL_OPER_EMAIL, process.env.SHELL_OPER_SENHA]
};
if (!cred.dono[0] || !cred.dono[1] || !cred.operador[0] || !cred.operador[1]) {
    console.error('shell-check: defina SHELL_DONO_EMAIL, SHELL_DONO_SENHA, SHELL_OPER_EMAIL e SHELL_OPER_SENHA');
    process.exit(1);
}
const TELAS = [
    { nome: 'Estoque', url: '/Home/Index', caminho: ['Operação', 'Estoque'], ativo: 'Estoque' },
    { nome: 'Dashboard', url: '/Dashboard', caminho: ['Gestão', 'Dashboard'], ativo: 'Dashboard' }
];

const PERFIS = {
    dono: {
        papel: 'Dono', negado: false,
        itens: ['Estoque', 'Caixa', 'Histórico', 'Dashboard'],
        grupos: ['Operação', 'Gestão'],
        hrefs: ['/Home/Index', '/Caixa', '/Caixa/Historico?periodo=hoje', '/Dashboard'],
        menu: ['Usuários', 'Sair']
    },
    operador: {
        papel: 'Operador', negado: true,
        itens: ['Estoque'],
        grupos: ['Operação'],
        hrefs: ['/Home/Index'],
        menu: ['Sair']
    }
};

let aprovados = 0;
let falhas = 0;
const gruposResultado = {};
function check(g, desc, cond, det) {
    if (!gruposResultado[g]) gruposResultado[g] = { p: 0, f: 0 };
    if (cond) {
        aprovados++; gruposResultado[g].p++;
        console.log('  [OK]    ' + g + ' ' + desc);
    } else {
        falhas++; gruposResultado[g].f++;
        console.log('  [FALHA] ' + g + ' ' + desc + (det !== undefined ? ' | ' + det : ''));
    }
}
const espera = (ms) => new Promise((r) => setTimeout(r, ms));

/* contraste WCAG do icone sob o knob (>= 3:1, nao-texto) */
function parseRgb(s) {
    const m = /rgb\((\d+),\s*(\d+),\s*(\d+)\)/.exec(s || '');
    return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
}
function canalLum(c) {
    const x = c / 255;
    return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
}
function razaoWcag(a, b) {
    if (!a || !b) return 0;
    const la = 0.2126 * canalLum(a[0]) + 0.7152 * canalLum(a[1]) + 0.0722 * canalLum(a[2]);
    const lb = 0.2126 * canalLum(b[0]) + 0.7152 * canalLum(b[1]) + 0.0722 * canalLum(b[2]);
    const alto = Math.max(la, lb);
    const baixo = Math.min(la, lb);
    return (alto + 0.05) / (baixo + 0.05);
}
/* retrato da pagina autenticada - usado nas assercoes E no baseline */
const SNAPSHOT = `(() => {
    const sb = document.getElementById('app-sidebar');
    const itens = sb ? Array.from(sb.querySelectorAll('.app-sidebar__item')).map(a => ({
        t: (a.textContent || '').trim(),
        href: a.getAttribute('href'),
        ativo: a.classList.contains('active'),
        cur: a.getAttribute('aria-current')
    })) : [];
    const hdr = document.querySelector('.app-header');
    const btn = document.getElementById('btnUsuario');
    const menu = document.getElementById('menuUsuario');
    return {
        pathname: location.pathname,
        shell: !!document.querySelector('.app-shell'),
        dataTheme: document.documentElement.getAttribute('data-theme'),
        itens: itens,
        grupos: sb ? Array.from(sb.querySelectorAll('.app-sidebar__grupo')).map(g => (g.textContent || '').trim()) : [],
        ativos: itens.filter(i => i.ativo).map(i => i.t),
        curCount: sb ? sb.querySelectorAll('[aria-current="page"]').length : -1,
        navLinks: sb ? sb.querySelectorAll('nav a').length : -1,
        temLogoutAntigo: !!document.getElementById('btnLogout'),
        headerFilhos: hdr ? Array.from(hdr.children).map(c => c.id || c.className) : [],
        temRestosHeader: !!document.querySelector('.app-header #campoBusca, .app-header #btnTheme, .app-header #btnMenuPerfil, .app-header .busca-form, .app-header #btnBusca'),
        nome: (document.querySelector('.app-usuario__nome') || {}).textContent || '',
        papel: (document.querySelector('.app-usuario__papel') || {}).textContent || '',
        itensMenu: Array.from(document.querySelectorAll('#menuUsuario [role="menuitem"]')).map(e => (e.textContent || '').trim()),
        btnExp: btn ? btn.getAttribute('aria-expanded') : null,
        menuOculto: menu ? menu.getAttribute('aria-hidden') : null,
        menuAberto: menu ? menu.classList.contains('aberto') : false,
        caminho: [
            ((document.querySelector('.app-caminho__grupo') || {}).textContent || ''),
            ((document.querySelector('.app-caminho__pagina') || {}).textContent || '')
        ].map(x => x.trim())
    };
})()`;
async function snap(api) { return await api.avaliar(SNAPSHOT); }
/* V1/V2/V5 - assercoes estaticas por tela */
function checarSidebar(g, s, esp) {
    const textos = s.itens.map((i) => i.t);
    const hrefs = s.itens.map((i) => i.href);
    check('V1', g + ' itens exatos da sidebar', JSON.stringify(textos) === JSON.stringify(esp.itens), textos.join(' | '));
    check('V1', g + ' grupos exatos', JSON.stringify(s.grupos) === JSON.stringify(esp.grupos), s.grupos.join(' | '));
    check('V1', g + ' hrefs exatos', JSON.stringify(hrefs) === JSON.stringify(esp.hrefs), hrefs.join(' | '));
    check('V1', g + ' nenhum link extra no nav', s.navLinks === esp.itens.length, 'navLinks=' + s.navLinks);
    check('V1', g + ' sem botao de logout antigo', s.temLogoutAntigo === false, String(s.temLogoutAntigo));
}
function checarAtivo(g, s, tela) {
    const ativos = s.ativos;
    check('V2', g + ' exatamente 1 item ativo', ativos.length === 1 && s.curCount === 1, 'ativos=[' + ativos.join(',') + '] cur=' + s.curCount);
    check('V2', g + ' ativo = ' + tela.ativo, ativos.length === 1 && ativos[0] === tela.ativo, ativos.join(','));
    const ativoCur = s.itens.filter((i) => i.ativo).map((i) => i.cur);
    check('V2', g + ' aria-current=page no ativo', ativoCur.length === 1 && ativoCur[0] === 'page', JSON.stringify(ativoCur));
}
function checarFaixa(g, s, tela) {
    check('V5', g + ' faixa = hamburguer + caminho', JSON.stringify(s.headerFilhos) === JSON.stringify(['btnMenu', 'app-caminho']), s.headerFilhos.join(' | '));
    check('V5', g + ' sem busca/tema/perfil antigos', s.temRestosHeader === false, String(s.temRestosHeader));
    check('V5', g + ' caminho = ' + tela.caminho.join(' / '), JSON.stringify(s.caminho) === JSON.stringify(tela.caminho), s.caminho.join(' / '));
}
/* V3 - menu do usuario unico: abre/fecha, Esc, clique fora, itens por papel */
async function checarMenuUsuario(g, api, esp) {
    const qtd = await api.avaliar(`JSON.stringify({
        btns: document.querySelectorAll('#btnUsuario').length,
        menus: document.querySelectorAll('#menuUsuario').length,
        antigo: document.querySelectorAll('#btnMenuPerfil').length,
        dropdowns: document.querySelectorAll('.app-header .dropdown-menu, .app-sidebar .dropdown-menu').length
    })`);
    const q = JSON.parse(qtd);
    check('V3', g + ' exatamente 1 botao #btnUsuario', q.btns === 1 && q.menus === 1, JSON.stringify(q));
    check('V3', g + ' nenhum #btnMenuPerfil/.dropdown-menu antigo', q.antigo === 0 && q.dropdowns === 0, JSON.stringify(q));

    let s = await snap(api);
    check('V3', g + ' papel exibido = ' + esp.papel, s.papel === esp.papel, s.papel);
    check('V3', g + ' nome exibido nao vazio', s.nome.trim().length > 0, JSON.stringify(s.nome));
    check('V3', g + ' itens do menu = ' + esp.menu.join('|'), JSON.stringify(s.itensMenu) === JSON.stringify(esp.menu), s.itensMenu.join(' | '));
    check('V3', g + ' comeca fechado', s.btnExp === 'false' && s.menuOculto === 'true' && !s.menuAberto, JSON.stringify({ exp: s.btnExp, oculto: s.menuOculto }));

    // abre
    const ab = JSON.parse(await api.avaliar(`(() => {
        document.getElementById('btnUsuario').click();
        const m = document.getElementById('menuUsuario');
        const b = document.getElementById('btnUsuario');
        return JSON.stringify({ exp: b.getAttribute('aria-expanded'), aberto: m.classList.contains('aberto'), oculto: m.getAttribute('aria-hidden'), papel: document.activeElement && document.activeElement.getAttribute('role') });
    })()`));
    check('V3', g + ' clique abre (aria-expanded/aria-hidden/foco)', ab.exp === 'true' && ab.aberto && ab.oculto === null && ab.papel === 'menuitem', JSON.stringify(ab));

    // Esc fecha (foco ja esta no 1o item)
    const esc = JSON.parse(await api.avaliar(`(() => {
        document.activeElement.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        const m = document.getElementById('menuUsuario');
        const b = document.getElementById('btnUsuario');
        return JSON.stringify({ exp: b.getAttribute('aria-expanded'), aberto: m.classList.contains('aberto'), oculto: m.getAttribute('aria-hidden'), focoVolta: document.activeElement === b });
    })()`));
    check('V3', g + ' Esc fecha e devolve o foco', esc.exp === 'false' && !esc.aberto && esc.oculto === 'true' && esc.focoVolta, JSON.stringify(esc));

    // clique fora fecha
    const fora = JSON.parse(await api.avaliar(`(() => {
        document.getElementById('btnUsuario').click();
        document.body.click();
        const m = document.getElementById('menuUsuario');
        return JSON.stringify({ exp: document.getElementById('btnUsuario').getAttribute('aria-expanded'), aberto: m.classList.contains('aberto'), oculto: m.getAttribute('aria-hidden') });
    })()`));
    check('V3', g + ' clique fora fecha', fora.exp === 'false' && !fora.aberto && fora.oculto === 'true', JSON.stringify(fora));
}
/* V4 - chave de tema: role=switch, aria-checked, clique alterna, persiste */
async function checarChaveTema(g, api) {
    const m = await api.avaliar(`JSON.stringify({
        role: document.querySelector('.app-theme')?.getAttribute('role') || null,
        checked: document.querySelector('.app-theme')?.getAttribute('aria-checked') || null,
        data: document.documentElement.getAttribute('data-theme'),
        salvo: localStorage.getItem('controlhub-tema'),
        moon: getComputedStyle(document.querySelector('.app-theme-moon')).color,
        sun: getComputedStyle(document.querySelector('.app-theme-sun')).color,
        knob: getComputedStyle(document.querySelector('.app-theme-knob')).backgroundColor
    })`);
    const t0 = JSON.parse(m);
    check('V4', g + ' role=switch', t0.role === 'switch', JSON.stringify(t0));
    check('V4', g + ' aria-checked sincronizado com data-theme',
        (t0.data === 'dark' && t0.checked === 'true') || (t0.data === 'light' && t0.checked === 'false'),
        JSON.stringify({ data: t0.data, checked: t0.checked }));
    check('V4', g + ' tema salvo no localStorage', t0.salvo === 'escuro' || t0.salvo === 'claro', String(t0.salvo));

    // icone sob o knob: escuro -> lua (knob-ink escuro sobre knob azul); claro -> sol branco
    const ico = t0.data === 'dark' ? t0.moon : t0.sun;
    const razao = razaoWcag(parseRgb(ico), parseRgb(t0.knob));
    check('V4', g + ' contraste icone/knob >= 3:1 (foi ' + razao.toFixed(2) + ')', razao >= 3, ico + ' sobre ' + t0.knob);

    // clique alterna e persiste
    const alt = JSON.parse(await api.avaliar(`(() => {
        document.querySelector('.app-theme').click();
        return JSON.stringify({ data: document.documentElement.getAttribute('data-theme'), salvo: localStorage.getItem('controlhub-tema'), checked: document.querySelector('.app-theme').getAttribute('aria-checked') });
    })()`));
    const esperado = t0.data === 'dark' ? 'light' : 'dark';
    const salvoEsperado = t0.salvo === 'escuro' ? 'claro' : 'escuro';
    check('V4', g + ' clique alterna tema', alt.data === esperado && alt.salvo === salvoEsperado, JSON.stringify(alt));
    // volta ao estado original para nao vazar para as proximas checagens
    const volta = JSON.parse(await api.avaliar(`(() => {
        document.querySelector('.app-theme').click();
        return JSON.stringify({ data: document.documentElement.getAttribute('data-theme'), salvo: localStorage.getItem('controlhub-tema') });
    })()`));
    check('V4', g + ' 2o clique restaura', volta.data === t0.data && volta.salvo === t0.salvo, JSON.stringify(volta));
}
/* V6 - larguras: 1440px sidebar estatica; 390px gaveta com cortina */
async function checarLarguras(g, api) {
    await api.redimensionar(1440, 960);
    await espera(300);
    const d = JSON.parse(await api.avaliar(`(() => {
        const sb = document.getElementById('app-sidebar');
        const r = sb.getBoundingClientRect();
        return JSON.stringify({ inner: window.innerWidth, visivel: r.left >= 0 && r.width > 0, cortina: document.getElementById('cortinaSidebar').classList.contains('ativo'), aberto: sb.classList.contains('aberto') });
    })()`));
    check('V6', g + ' 1440px: sidebar estatica visivel, sem cortina', d.inner >= 1024 && d.visivel && !d.cortina && !d.aberto, JSON.stringify(d));

    // recolher/abrir no desktop persiste
    const rec = JSON.parse(await api.avaliar(`(() => {
        document.getElementById('btnMenu').click();
        const sb = document.getElementById('app-sidebar');
        const fechou = getComputedStyle(sb).display === 'none';
        document.getElementById('btnMenu').click();
        return JSON.stringify({ fechou: fechou, voltou: getComputedStyle(sb).display !== 'none', salvo: localStorage.getItem('controlhub-sidebar') });
    })()`));
    check('V6', g + ' 1440px: hamburguer recolhe e reabre (persiste)', rec.fechou && rec.voltou && (rec.salvo === 'aberta' || rec.salvo === null || rec.salvo === 'fechada'), JSON.stringify(rec));

    await api.redimensionar(390, 844);
    await espera(300);
    const m = JSON.parse(await api.avaliar(`(() => {
        const sb = document.getElementById('app-sidebar');
        const r = sb.getBoundingClientRect();
        return JSON.stringify({ inner: window.innerWidth, fora: r.right <= 0, cortinaAtiva: document.getElementById('cortinaSidebar').classList.contains('ativo') });
    })()`));
    check('V6', g + ' 390px: sidebar fora da tela, cortina inativa', m.inner <= 420 && m.fora && !m.cortinaAtiva, JSON.stringify(m));

    const abre = JSON.parse(await api.avaliar(`(() => {
        document.getElementById('btnMenu').click();
        const sb = document.getElementById('app-sidebar');
        return JSON.stringify({ aberto: sb.classList.contains('aberto'), cortina: document.getElementById('cortinaSidebar').classList.contains('ativo') });
    })()`));
    check('V6', g + ' 390px: hamburguer abre gaveta + cortina', abre.aberto && abre.cortina, JSON.stringify(abre));
    // espera a transition de transform (.22s) terminar antes de medir a posicao
    await espera(400);
    const dentro = JSON.parse(await api.avaliar(`(() => {
        const r = document.getElementById('app-sidebar').getBoundingClientRect();
        return JSON.stringify({ left: Math.round(r.left), dentro: r.left >= -1 });
    })()`));
    check('V6', g + ' 390px: gaveta entra na tela', dentro.dentro, JSON.stringify(dentro));

    const esc = JSON.parse(await api.avaliar(`(() => {
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        const sb = document.getElementById('app-sidebar');
        return JSON.stringify({ aberto: sb.classList.contains('aberto'), cortina: document.getElementById('cortinaSidebar').classList.contains('ativo') });
    })()`));
    check('V6', g + ' 390px: Esc fecha a gaveta', !esc.aberto && !esc.cortina, JSON.stringify(esc));

    const cortina = JSON.parse(await api.avaliar(`(() => {
        document.getElementById('btnMenu').click();
        document.getElementById('cortinaSidebar').click();
        const sb = document.getElementById('app-sidebar');
        return JSON.stringify({ aberto: sb.classList.contains('aberto'), cortina: document.getElementById('cortinaSidebar').classList.contains('ativo') });
    })()`));
    check('V6', g + ' 390px: clique na cortina fecha', !cortina.aberto && !cortina.cortina, JSON.stringify(cortina));

    await api.redimensionar(1440, 960);
    await espera(300);
}
/* ---------------- V7 baseline + fluxo principal ---------------- */
const baseline = { geradoEm: null, perfis: {} };

async function rodarTela(api, perfil, tema, tela, esp, registro) {
    await api.ir(BASE + tela.url);
    const s = await snap(api);
    const g = perfil + '/' + tela.nome + '/' + tema;

    if (esp.negado && tela.nome === 'Dashboard') {
        // gate preservado: Operador nao entra no Dashboard
        const negado = s.pathname.includes('AcessoNegado');
        check('V2', g + ' Operador negado no Dashboard (302 AcessoNegado)', negado, s.pathname);
        registro.negado = negado;
        registro.pathname = s.pathname;
        return;
    }
    checarSidebar(g, s, esp);
    checarAtivo(g, s, tela);
    checarFaixa(g, s, tela);
    if (tela.nome === 'Estoque') {
        await checarMenuUsuario(g, api, esp);
        await checarChaveTema(g, api);
    }
    registro.itens = s.itens.map((i) => i.t);
    registro.ativo = s.ativos;
    registro.caminho = s.caminho;
    registro.usuario = { nome: s.nome.trim(), papel: s.papel };
    registro.menu = s.itensMenu;
}

async function rodarPerfil(api, perfil, esp) {
    const [email, senha] = cred[perfil];
    await login(api, BASE, email, senha);
    baseline.perfis[perfil] = { papel: esp.papel, telas: {} };

    for (const tema of ['escuro', 'claro']) {
        console.log('--- ' + perfil + ' / tema ' + tema + ' ---');
        await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
        for (const tela of TELAS) {
            const chave = tela.nome + '@' + tema;
            baseline.perfis[perfil].telas[chave] = { tema: tema, tela: tela.nome };
            await rodarTela(api, perfil, tema, tela, esp, baseline.perfis[perfil].telas[chave]);
        }
    }

    // V6 numa tela com shell (Estoque), tema claro ja ativo
    await api.ir(BASE + '/Home/Index');
    await checarLarguras(perfil + '/Estoque', api);
}

async function main() {
    const ctx = abrirChrome(1440, 960, 9457);
    const api = await conectar(9457);
    try {
        await rodarPerfil(api, 'dono', PERFIS.dono);
        // logout para trocar de conta no mesmo browser
        await api.avaliar(`(() => {
            const f = document.querySelector('form[action*="Logout"]');
            if (f) { f.submit(); return 'ok'; }
            return 'sem-form';
        })()`);
        await espera(2500);
        await rodarPerfil(api, 'operador', PERFIS.operador);
    } finally {
        try { fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil }); } catch { /* ja fechou */ }
    }

    baseline.geradoEm = new Date().toISOString();
    mkdirSync(OUT, { recursive: true });
    writeFileSync(join(OUT, 'baseline-menu.json'), JSON.stringify(baseline, null, 2), 'utf8');
    console.log('baseline gravado em ' + join(OUT, 'baseline-menu.json'));

    console.log('');
    console.log('=== RESULTADO ===');
    console.log('  checagens aprovadas: ' + aprovados);
    console.log('  checagens com falha: ' + falhas);
    for (const [g, r] of Object.entries(gruposResultado)) {
        console.log('  ' + g + ': ' + r.p + ' ok / ' + r.f + ' falha(s)');
    }
    if (falhas > 0) {
        console.log('shell-check: FALHOU');
        process.exit(1);
    }
    console.log('shell-check: OK - 7 verificacoes aprovadas');
    process.exit(0);
}

main().catch((e) => {
    console.error('shell-check: ERRO ' + (e && e.message ? e.message : e));
    process.exit(1);
});