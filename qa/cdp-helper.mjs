/*
 * qa/cdp-helper.mjs - infraestrutura comum dos checks visuais do shell.
 * Sobe o Chrome headless, fala por DevTools Protocol (WebSocket/fetch globais
 * do Node - sem dependencia npm), faz login e mede o layout.
 *
 * Uso como biblioteca:
 *   import { abrirChrome, conectar, login, medir } from './cdp-helper.mjs';
 *
 * Credenciais e porta vem do ambiente/argumentos - NUNCA gravadas aqui.
 */
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const CHROME = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';

export function abrirChrome(largura, altura, porta = 9444) {
    const perfil = mkdtempSync(join(tmpdir(), 'shell-chrome-'));
    const chrome = spawn(CHROME, [
        '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
        `--remote-debugging-port=${porta}`, `--window-size=${largura},${altura}`,
        '--hide-scrollbars', '--force-device-scale-factor=1',
        '--user-data-dir=' + perfil, 'about:blank'
    ], { stdio: 'ignore' });
    return { chrome, perfil, porta };
}

export async function conectar(porta) {
    const espera = ms => new Promise(r => setTimeout(r, ms));
    let wsUrl = null;
    for (let i = 0; i < 60; i++) {
        try {
            const r = await fetch(`http://127.0.0.1:${porta}/json/list`);
            const lista = await r.json();
            const pagina = lista.find(t => t.type === 'page');
            if (pagina) { wsUrl = pagina.webSocketDebuggerUrl; break; }
        } catch { /* subindo */ }
        await espera(300);
    }
    if (!wsUrl) throw new Error('chrome nao expôs o alvo de depuracao');
    const ws = new WebSocket(wsUrl);
    await new Promise((ok, err) => { ws.onopen = ok; ws.onerror = err; });
    let seq = 0;
    const pendentes = new Map();
    // Assinantes de EVENTOS CDP (sem id): Network.responseReceived,
    // Runtime.consoleAPICalled, Log.entryAdded etc. O nav-smoke e o
    // diagnostico usam isto para contar status >= 400 e erros de console.
    const assinantes = [];
    const aoEvento = fn => { assinantes.push(fn); return fn; };
    ws.onmessage = ev => {
        const m = JSON.parse(ev.data);
        if (m.id && pendentes.has(m.id)) {
            const { ok, erro } = pendentes.get(m.id);
            pendentes.delete(m.id);
            if (m.error) erro(new Error(m.error.message)); else ok(m.result);
        } else if (m.method) {
            for (const fn of assinantes) { try { fn(m); } catch { /* ruído do coletor */ } }
        }
    };
    const cmd = (method, params = {}) => {
        const id = ++seq;
        ws.send(JSON.stringify({ id, method, params }));
        return new Promise((ok, erro) => pendentes.set(id, { ok, erro }));
    };
    const avaliar = async (expr) => {
        const r = await cmd('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
        if (r.exceptionDetails) throw new Error('JS: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
        return r.result.value;
    };
    const ir = async (url) => {
        await cmd('Page.navigate', { url });
        const caminho = new URL(url).pathname;
        for (let i = 0; i < 60; i++) {
            try {
                const v = await avaliar('JSON.stringify([document.readyState, location.pathname])');
                const [estado, atual] = JSON.parse(v);
                if (estado === 'complete' && (atual === caminho || i > 20)) break;
            } catch { /* navegando */ }
            await espera(250);
        }
        await espera(700);
    };
    const larguraAtual = async () => (await cmd('Runtime.evaluate', { expression: 'window.innerWidth', returnByValue: true })).result.value;
    const redimensionar = async (w, h) => {
        await cmd('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: w < 1024 });
    };
    return { ws, cmd, avaliar, ir, larguraAtual, redimensionar, aoEvento };
}

export async function login(api, base, email, senha) {
    await api.ir(base + '/Account/Login');
    // Já autenticado, /Account/Login redireciona e o formulário não existe:
    // sai pela rota POST /Account/Logout (formulário do layout, com token
    // antiforgery embutido) antes de logar de novo.
    const temForm = await api.avaliar(`!!document.querySelector('input[name="Email"]')`);
    if (!temForm) {
        await api.ir(base + '/Dashboard');
        const saiu = await api.avaliar(`(() => {
            const f = document.querySelector('form[action*="Logout" i]');
            if (!f) return 'sem-form';
            f.submit(); return 'saiu';
        })()`);
        if (saiu !== 'saiu') throw new Error('login: sem form de logout em ' + base);
        await new Promise(r => setTimeout(r, 1500));
        await api.ir(base + '/Account/Login');
    }
    await api.avaliar(`(async () => {
        for (let i = 0; i < 20; i++) {
            if (document.querySelector('input[name="Email"]')) return 'achou';
            await new Promise(ok => setTimeout(ok, 150));
        }
        return 'timeout';
    })()`);
    const situacao = await api.avaliar(`(() => {
        const e = document.querySelector('input[name="Email"]');
        const s = document.querySelector('input[name="Senha"]');
        if (!e || !s) {
            return 'sem-inputs href=' + location.href + ' forms=' + document.querySelectorAll('form').length;
        }
        // Preenche e submete o form QUE CONTÉM os campos (a página de login
        // pode coexistir com outros forms no DOM dependendo do estado).
        const f = e.form;
        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        set.call(e, ${JSON.stringify(email)}); e.dispatchEvent(new Event('input', { bubbles: true }));
        set.call(s, ${JSON.stringify(senha)}); s.dispatchEvent(new Event('input', { bubbles: true }));
        f.submit(); return 'ok';
    })()`);
    if (situacao !== 'ok') throw new Error('login: formulário inesperado (' + situacao + ') para ' + email);
    await new Promise(r => setTimeout(r, 2500));
    const apos = String(await api.avaliar('location.href'));
    if (apos.includes('/Account/Login')) throw new Error('login falhou para ' + email);
}

export function fechar(ctx) {
    try { ctx.ws.close(); } catch { /* ja fechado */ }
    try { execFileSync('taskkill', ['/PID', String(ctx.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* ja morreu */ }
    try { rmSync(ctx.perfil, { recursive: true, force: true }); } catch { /* melhor esforco */ }
}
