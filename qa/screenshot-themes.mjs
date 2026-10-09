/*
 * qa/screenshot-themes.mjs — PASSO 4: prints das 6 telas nos DOIS temas.
 * Sobe o Chrome headless e fala com ele pela DevTools Protocol (Node global
 * WebSocket/fetch - sem dependencia npm). Grava 12 PNGs em --out.
 * Credenciais vêm do ambiente (PRINTS_EMAIL / PRINTS_SENHA) - nunca aqui.
 *
 * Uso:
 *   node qa/screenshot-themes.mjs --base http://localhost:5311 --out "E:\Sistema de Estoque\prints"
 */
import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const args = process.argv.slice(2);
function arg(nome, padrao) {
    const i = args.indexOf(nome);
    return i >= 0 && args[i + 1] ? args[i + 1] : padrao;
}
const BASE = arg('--base', 'http://localhost:5311').replace(/\/+$/, '');
const OUT = arg('--out', 'E:\\Sistema de Estoque\\prints');
const EMAIL = process.env.PRINTS_EMAIL;
const SENHA = process.env.PRINTS_SENHA;
const CHROME = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';

if (!EMAIL || !SENHA) {
    console.error('screenshot-themes: defina PRINTS_EMAIL e PRINTS_SENHA no ambiente');
    process.exit(1);
}
mkdirSync(OUT, { recursive: true });
const perfil = mkdtempSync(join(tmpdir(), 'prints-chrome-'));
const chrome = spawn(CHROME, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--remote-debugging-port=9333', '--window-size=1440,960', '--hide-scrollbars',
    '--user-data-dir=' + perfil, 'about:blank'
], { stdio: 'ignore' });

const esperar = ms => new Promise(r => setTimeout(r, ms));

async function alvo() {
    for (let i = 0; i < 50; i++) {
        try {
            const r = await fetch('http://127.0.0.1:9333/json/list');
            const lista = await r.json();
            const pagina = lista.find(t => t.type === 'page');
            if (pagina) return pagina.webSocketDebuggerUrl;
        } catch { /* chrome ainda subindo */ }
        await esperar(300);
    }
    throw new Error('chrome nao expôs o alvo de depuracao');
}

const ws = new WebSocket(await alvo());
await new Promise((ok, err) => { ws.onopen = ok; ws.onerror = err; });
let seq = 0;
const pendentes = new Map();
ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pendentes.has(m.id)) {
        const { ok, erro } = pendentes.get(m.id);
        pendentes.delete(m.id);
        if (m.error) erro(new Error(m.error.message)); else ok(m.result);
    }
};
function cmd(method, params = {}) {
    const id = ++seq;
    ws.send(JSON.stringify({ id, method, params }));
    return new Promise((ok, erro) => pendentes.set(id, { ok, erro }));
}

async function ir(url) {
    await cmd('Page.navigate', { url });
    const caminho = new URL(url).pathname;
    for (let i = 0; i < 60; i++) {
        try {
            const r = await cmd('Runtime.evaluate', {
                expression: 'JSON.stringify([document.readyState, location.pathname])',
                returnByValue: true
            });
            const [estado, atual] = JSON.parse(r.result.value);
            if (estado === 'complete' && (atual === caminho || i > 20)) break;
        } catch { /* navegacao em andamento */ }
        await esperar(250);
    }
    await esperar(900); // JS das telas (dashboard carrega por AJAX)
}
async function avaliar(expr) {
    const r = await cmd('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error('JS: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
    return r.result.value;
}
async function print(nome) {
    const r = await cmd('Page.captureScreenshot', { format: 'png' });
    writeFileSync(join(OUT, nome), Buffer.from(r.data, 'base64'));
    console.log('  [OK] ' + nome);
}
const gravarTema = valor => avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(valor)}); 'ok'`);

let falhas = 0;
try {
    // 1) LOGIN nos dois temas (ainda deslogado)
    await ir(BASE + '/Account/Login');
    for (const t of ['escuro', 'claro']) {
        await gravarTema(t);
        await cmd('Page.reload', { ignoreCache: true });
        await esperar(900);
        await print('login-' + t + '.png');
    }

    // 2) entra como Dono (submit nativo: leva o antiforgery junto)
    await avaliar(`(() => {
        const f = document.querySelector('form');
        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        const e = f.querySelector('input[name="Email"]');
        const s = f.querySelector('input[name="Senha"]');
        set.call(e, ${JSON.stringify(EMAIL)}); e.dispatchEvent(new Event('input', { bubbles: true }));
        set.call(s, ${JSON.stringify(SENHA)}); s.dispatchEvent(new Event('input', { bubbles: true }));
        f.submit(); return 'ok';
    })()`);
    await esperar(2500);
    const apos = String(await avaliar('location.href'));
    if (apos.includes('/Account/Login')) throw new Error('login falhou - confira PRINTS_EMAIL/PRINTS_SENHA');

    // 3) as 5 telas internas nos dois temas
    const telas = [
        ['estoque', BASE + '/'],
        ['caixa-modal', BASE + '/Caixa'],
        ['historico', BASE + '/Caixa/Historico?periodo=hoje'],
        ['usuarios', BASE + '/Usuarios'],
        ['dashboard', BASE + '/Dashboard']
    ];
    for (const t of ['escuro', 'claro']) {
        await gravarTema(t);
        for (const [nome, url] of telas) {
            await ir(url);
            if (nome === 'caixa-modal') {
                await avaliar(`(() => {
                    const m = document.getElementById('modalPagamento');
                    if (m) { m.style.display = 'flex'; }
                    const sp = document.getElementById('secaoPagamento');
                    if (sp) { sp.style.display = 'block'; }
                    return 'ok';
                })()`);
                await esperar(400);
            }
            await print(nome + '-' + t + '.png');
        }
    }
} catch (e) {
    falhas++;
    console.error('screenshot-themes: ERRO ' + e.message);
} finally {
    try { ws.close(); } catch { /* ja fechado */ }
    try { execFileSync('taskkill', ['/PID', String(chrome.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* ja morreu */ }
    try { rmSync(perfil, { recursive: true, force: true }); } catch { /* melhor esforco */ }
}
if (falhas > 0) {
    console.log('screenshot-themes: FALHOU');
    process.exit(1);
}
console.log('screenshot-themes: OK - 12 prints em ' + OUT);