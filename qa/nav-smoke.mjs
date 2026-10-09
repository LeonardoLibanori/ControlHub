/*
 * qa/nav-smoke.mjs - AJUSTE DE SHELL 2: guarda de navegacao e atalhos.
 *
 * Para Dono e Operador, nos DOIS temas, confere por CDP:
 *   N1 cada destino do menu (e "Usuarios" pelo menu do usuario, Dono) abre
 *      HTTP 200, com breadcrumb coerente, ZERO erros de console e ZERO
 *      requisicoes com status >= 400;
 *   N2 Alt+M abre o overlay em todas as telas e Esc o fecha;
 *   N3 Alt+C, Alt+E e Alt+H navegam para o destino certo;
 *   N4 o Operador nao ve itens fora da policy e /Dashboard da acesso negado.
 *
 * ATALHOS: o ajuste 2 exige que o handler case por event.code (KeyM/KeyC/...),
 * que e IDENTICO em qualquer layout de teclado. Por isso os testes disparam um
 * KeyboardEvent com o `code` real e um `key` de layout nao-US (ex.: 'µ' para
 * Alt+M): só um handler por event.code abre; um handler por e.key ('m') NAO
 * abriria - e e assim que a regressao fica presa. Disparar direto no document
 * (fonte do listener) tambem evia a fragilidade de entrega de input do Chrome
 * headless, sem abrir mao de exercitar o handler real.
 *
 * Credenciais SO por ambiente (nunca gravadas aqui). Sem a app no ar, falha.
 *   node qa/nav-smoke.mjs --base http://localhost:5311
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';

const argv = process.argv.slice(2);
function arg(nome, padrao) { const i = argv.indexOf(nome); return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao; }
const BASE = arg('--base', process.env.SHELL_BASE || 'http://localhost:5311').replace(/\/+$/, '');
const DONO = { email: process.env.SHELL_DONO_EMAIL || 'dono@teste.com', senha: process.env.SHELL_DONO_SENHA || '***REMOVIDO***' };
const OPER = { email: process.env.SHELL_OPER_EMAIL || 'operador@teste.com', senha: process.env.SHELL_OPER_SENHA || 'Operador@123' };

let ok = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { ok++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
const espera = ms => new Promise(r => setTimeout(r, ms));

function coletor(api) {
    const ev = [];
    api.aoEvento(m => ev.push(m));
    const erroTxt = m => {
        if (m.method === 'Runtime.consoleAPICalled') return m.params.args.map(a => a.value ?? a.description).join(' ');
        if (m.method === 'Log.entryAdded') return m.params.entry.text;
        return m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text;
    };
    return {
        limpar: () => { ev.length = 0; },
        erros: () => ev.filter(m =>
            (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') ||
            (m.method === 'Log.entryAdded' && m.params.entry.level === 'error') ||
            m.method === 'Runtime.exceptionThrown').map(erroTxt),
        ruins: () => ev.filter(m => m.method === 'Network.responseReceived')
            .map(m => m.params.response).filter(r => r.status >= 400).map(r => r.status + ' ' + r.url),
        statusDe: (filtro) => {
            const rs = ev.filter(m => m.method === 'Network.responseReceived')
                .map(m => m.params.response).filter(r => r.url.includes(filtro));
            return rs.length ? rs[rs.length - 1].status : null;
        }
    };
}

// Dispara um keydown no document com `code` real e `key` de layout nao-US, para
// provar que o handler usa event.code (detalhe no cabecalho).
async function tecla(api, { key, code, alt = true }) {
    await api.avaliar(`document.dispatchEvent(new KeyboardEvent('keydown', {
        key: ${JSON.stringify(key)}, code: ${JSON.stringify(code)},
        altKey: ${alt}, ctrlKey: false, shiftKey: false, bubbles: true, cancelable: true
    })); 'ok'`);
    await espera(350);
}
async function overlay(api) {
    return String(await api.avaliar(`(() => { const m = document.getElementById('modalAtalhos'); return m ? (m.classList.contains('show') ? 'ABERTO' : 'fechado') : 'sem-modal'; })()`));
}

// destinos do menu (itens da sidebar) por perfil, com o breadcrumb esperado
const DESTINOS = {
    dono: [
        { nome: 'Estoque', url: '/Home/Index', bc: 'Estoque' },
        { nome: 'Caixa', url: '/Caixa', bc: 'Caixa' },
        { nome: 'Histórico', url: '/Caixa/Historico?periodo=hoje', bc: 'Histórico' },
        { nome: 'Dashboard', url: '/Dashboard', bc: 'Dashboard' }
    ],
    operador: [
        { nome: 'Estoque', url: '/Home/Index', bc: 'Estoque' },
        { nome: 'Caixa', url: '/Caixa', bc: 'Caixa' }
    ]
};
const TELAS_ATALHO = {
    dono: ['/Home/Index', '/Caixa', '/Caixa/Historico?periodo=hoje', '/Dashboard'],
    operador: ['/Home/Index', '/Caixa']
};

async function testarDestinos(api, perfil, tema) {
    for (const d of DESTINOS[perfil]) {
        const c = coletor(api);
        c.limpar();
        await api.ir(BASE + d.url);
        await espera(600);
        const url = String(await api.avaliar('location.href'));
        const path = new URL(d.url, BASE).pathname;
        const st = c.statusDe(path);
        const bc = String(await api.avaliar(`(() => { const p = document.querySelector('.app-caminho__pagina'); return p ? p.textContent.trim() : ''; })()`));
        check(`${perfil}/${tema} ${d.nome}: HTTP 200`, st === 200 && !url.includes('AcessoNegado'), `status=${st} url=${url}`);
        check(`${perfil}/${tema} ${d.nome}: zero erros de console e zero >=400`, c.erros().length === 0 && c.ruins().length === 0,
            `erros=${JSON.stringify(c.erros())} ruins=${JSON.stringify(c.ruins())}`);
        if (d.bc) check(`${perfil}/${tema} ${d.nome}: breadcrumb contem "${d.bc}"`, bc.includes(d.bc), `bc="${bc}"`);
    }
}

async function testarUsuarios(api, perfil, tema) {
    const c = coletor(api);
    await api.ir(BASE + '/Home/Index'); await espera(500);
    c.limpar();
    const abriu = String(await api.avaliar(`(() => { const b=document.getElementById('btnUsuario'); if(b)b.click(); return document.getElementById('menuUsuario').classList.contains('aberto'); })()`));
    await espera(300);
    const item = String(await api.avaliar(`(() => { const it=[...document.querySelectorAll('#menuUsuario a')].find(a=>a.textContent.trim()==='Usuários'); if(it){it.click(); return 'clicou';} return 'sem-item'; })()`));
    await espera(1300);
    const url = String(await api.avaliar('location.href'));
    check(`${perfil}/${tema} Usuarios pelo menu do usuario abre 200`, abriu === 'true' && item === 'clicou' && url.includes('/Usuarios') && c.ruins().length === 0 && c.erros().length === 0,
        `menuAbriu=${abriu} item=${item} url=${url} ruins=${JSON.stringify(c.ruins())}`);
}

async function testarAltM(api, perfil, tema) {
    for (const url of TELAS_ATALHO[perfil]) {
        await api.ir(BASE + url); await espera(500);
        await tecla(api, { key: 'µ', code: 'KeyM', alt: true });   // 'µ' = Alt+M em layout nao-US
        // O modal do Bootstrap tem animacao fade; no headless o transitionend
        // demora/nao dispara, e hide() durante a abertura eh ignorado. Espera a
        // animacao assentar (em navegador real isso eh o fim natural do fade).
        await espera(900);
        const abriu = await overlay(api);
        check(`${perfil}/${tema} Alt+M abre overlay em ${url}`, abriu === 'ABERTO', abriu);
        await tecla(api, { key: 'Escape', code: 'Escape', alt: false });
        await espera(900);
        const fechou = await overlay(api);
        check(`${perfil}/${tema} Esc fecha overlay em ${url}`, fechou === 'fechado', fechou);
    }
}

async function testarNav(api, perfil, tema) {
    // cada atalho parte de uma tela DIFERENTE do destino (evita falso-positivo)
    await api.ir(BASE + '/Home/Index'); await espera(400);
    await tecla(api, { key: '¢', code: 'KeyC', alt: true }); await espera(1300);
    check(`${perfil}/${tema} Alt+C -> /Caixa`, String(await api.avaliar('location.href')).includes('/Caixa'), String(await api.avaliar('location.href')));

    await api.ir(BASE + '/Caixa'); await espera(400);
    await tecla(api, { key: '€', code: 'KeyE', alt: true }); await espera(1300);
    check(`${perfil}/${tema} Alt+E -> /Home/Index`, String(await api.avaliar('location.href')).includes('/Home/Index'), String(await api.avaliar('location.href')));

    await api.ir(BASE + '/Home/Index'); await espera(400);
    await tecla(api, { key: '˙', code: 'KeyH', alt: true }); await espera(1300);
    const urlH = String(await api.avaliar('location.href'));
    if (perfil === 'dono') check(`${perfil}/${tema} Alt+H -> /Caixa/Historico`, urlH.includes('/Caixa/Historico'), urlH);
    else check(`${perfil}/${tema} Alt+H nao navega (sem policy)`, urlH.includes('/Home/Index'), urlH);
}

async function testarPolicy(api, perfil, tema) {
    await api.ir(BASE + '/Home/Index'); await espera(500);
    const itens = String(await api.avaliar(`JSON.stringify([...document.querySelectorAll('.app-sidebar__item')].map(a => a.textContent.trim()))`));
    if (perfil === 'operador') {
        check(`${perfil}/${tema} ve so Estoque no menu`, itens === JSON.stringify(['Estoque']), itens);
        const temUsu = String(await api.avaliar(`!!document.querySelector('#menuUsuario a[href*="Usuarios"]')`));
        check(`${perfil}/${tema} NAO ve Usuarios no menu do usuario`, temUsu === 'false', temUsu);
        await api.ir(BASE + '/Dashboard'); await espera(800);
        const url = String(await api.avaliar('location.href'));
        check(`${perfil}/${tema} /Dashboard da acesso negado`, url.includes('AcessoNegado'), url);
    } else {
        check(`${perfil}/${tema} ve os 4 itens (Dono)`, itens === JSON.stringify(['Estoque', 'Caixa', 'Histórico', 'Dashboard']), itens);
    }
}

async function sair(api) {
    await api.avaliar(`(() => { const f=document.querySelector('form[action*="Logout"]'); if(f)f.submit(); return 'saiu'; })()`);
    await espera(1500);
}

async function main() {
    const ctx = abrirChrome(1440, 960, 9460);
    const api = await conectar(9460);
    await api.cmd('Network.enable'); await api.cmd('Log.enable'); await api.cmd('Runtime.enable');
    try {
        for (const tema of ['escuro', 'claro']) {
            console.log(`\n===== DONO / tema ${tema} =====`);
            await login(api, BASE, DONO.email, DONO.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            await testarDestinos(api, 'dono', tema);
            await testarUsuarios(api, 'dono', tema);
            await testarAltM(api, 'dono', tema);
            await testarNav(api, 'dono', tema);
            await testarPolicy(api, 'dono', tema);
            await sair(api);

            console.log(`\n===== OPERADOR / tema ${tema} =====`);
            await login(api, BASE, OPER.email, OPER.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            await testarDestinos(api, 'operador', tema);
            await testarAltM(api, 'operador', tema);
            await testarNav(api, 'operador', tema);
            await testarPolicy(api, 'operador', tema);
            await sair(api);
        }
    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }
    console.log(`\n=== nav-smoke: ${ok} ok / ${falhas} falha(s) ===`);
    if (falhas > 0) { console.log('nav-smoke: FALHOU'); process.exit(1); }
    console.log('nav-smoke: OK'); process.exit(0);
}
main().catch(e => { console.error('nav-smoke: ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });


