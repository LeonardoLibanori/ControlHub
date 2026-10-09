/* qa/_dash-dbg.mjs - debug do cabecalho do Dashboard. TEMP. */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
const BASE = 'http://localhost:5311';
const espera = ms => new Promise(r => setTimeout(r, ms));
const { chrome, ...ctx1 } = abrirChrome(1440, 960, 9471);
const api = await conectar(9471);
try {
    await login(api, BASE, 'dono@teste.com', '***REMOVIDO***');
    await api.cmd('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
    await api.ir(BASE + '/Dashboard'); await espera(700);
    const html = String(await api.avaliar(`document.querySelector('.dash-cabecalho').outerHTML.slice(0, 400)`));
    console.log('HTML do cabecalho:\n', html);
    const info = String(await api.avaliar(`(() => {
        const h = document.querySelector('.dash-cabecalho');
        const cs = getComputedStyle(h);
        const t = document.querySelector('.dash-cabecalho .dash-titulo');
        const s = document.querySelector('.dash-cabecalho .dash-periodo');
        const cont = document.querySelector('.dash-container') || h.parentElement;
        const tb = t.getBoundingClientRect(), sb = s.getBoundingClientRect(), hb = h.getBoundingClientRect();
        return JSON.stringify({
            innerWidth: window.innerWidth,
            headerW: Math.round(hb.width), headerDisplay: cs.display, headerFlexWrap: cs.flexWrap,
            containerW: Math.round(cont.getBoundingClientRect().width), containerClass: cont.className,
            titulo: { top: Math.round(tb.top), bottom: Math.round(tb.bottom), left: Math.round(tb.left), w: Math.round(tb.width) },
            seletor: { top: Math.round(sb.top), bottom: Math.round(sb.bottom), left: Math.round(sb.left), w: Math.round(sb.width) }
        }, null, 0);
    })()`));
    console.log('computed:', info);
} finally {
    fechar({ ws: api.ws, pid: chrome.pid, perfil: ctx1.perfil });
    chrome.kill();
}
process.exit(0);
