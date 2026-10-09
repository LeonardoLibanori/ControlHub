/* qa/_dash-head-verify.mjs - prova CDP do cabeçalho do Dashboard (AJUSTE 3). */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
const BASE = 'http://localhost:5311';
const espera = ms => new Promise(r => setTimeout(r, ms));
const MEDIR = `(() => {
    const r = el => { const b = el.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, h: b.height, cy: b.top + b.height/2 }; };
    const header = document.querySelector('.dash-cabecalho');
    const titulo = document.querySelector('.dash-cabecalho .dash-titulo');
    const seletor = document.querySelector('.dash-cabecalho .dash-periodo');
    const cartao = document.querySelector('.dash-periodo-total');
    if (!header || !titulo || !seletor || !cartao) return JSON.stringify({ erro: 'elementos ausentes', header: !!header, titulo: !!titulo, seletor: !!seletor, cartao: !!cartao });
    const H = r(header), T = r(titulo), S = r(seletor), C = r(cartao);
    const gap = C.top - Math.max(S.bottom, T.bottom);
    const dCentro = Math.abs(S.cy - T.cy);
    const txt = document.body.innerText || '';
    const temConferencia = /confer/i.test(txt);
    const de = document.documentElement;
    return JSON.stringify({ tituloCy: Math.round(T.cy*10)/10, seletorCy: Math.round(S.cy*10)/10, headerBottom: Math.round(H.bottom*10)/10, cartaoTop: Math.round(C.top*10)/10, gap, dCentro, temConferencia, scrollW: de.scrollWidth, clientW: de.clientWidth, overflow: de.scrollWidth - de.clientWidth });
})()`;

async function main() {
    const ctx = abrirChrome(1440, 960, 9480);
    const api = await conectar(9480);
    try {
        await login(api, BASE, 'dono@teste.com', '***REMOVIDO***');
        console.log('login dono: OK');
        for (const w of [1440, 1024, 390]) {
            await api.cmd('Emulation.setDeviceMetricsOverride', { width: w, height: 900, deviceScaleFactor: 1, mobile: w < 1024 });
            await api.ir(BASE + '/Dashboard');
            await espera(700);
            const m = JSON.parse(String(await api.avaliar(MEDIR)));
            console.log(`largeura=${w} | gap=${m.gap}px | dCentro=${m.dCentro}px | confer=${m.temConferencia} | overflow=${m.overflow}`);
        }
    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
        ctx.chrome.kill();
    }
}
main().catch(e => { console.error('ERRO: ' + (e && e.message ? e.message : e)); process.exit(1); });
