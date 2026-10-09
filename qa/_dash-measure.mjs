/* qa/_dash-measure.mjs - medicao do cabecalho do Dashboard (AJUSTE 3). TEMP. */
import { abrirChrome, conectar, fechar } from './cdp-helper.mjs';
const BASE = 'http://localhost:5311';
const espera = ms => new Promise(r => setTimeout(r, ms));

const MEDIR = `(() => {
    const r = el => { const b = el.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, left: b.left, right: b.right, h: b.height, w: b.width, cy: b.top + b.height/2 }; };
    const header = document.querySelector('.dash-cabecalho');
    const titulo = document.querySelector('.dash-cabecalho .dash-titulo');
    const seletor = document.querySelector('.dash-cabecalho .dash-periodo');
    const cartao = document.querySelector('.dash-periodo-total');
    if (!header || !titulo || !seletor || !cartao) return JSON.stringify({ erro: 'elementos ausentes', header: !!header, titulo: !!titulo, seletor: !!seletor, cartao: !!cartao });
    const H = r(header), T = r(titulo), S = r(seletor), C = r(cartao);
    const maisBaixo = Math.max(S.bottom, T.bottom);
    const gap = C.top - maisBaixo;
    const diffCentro = Math.abs(S.cy - T.cy);
    const txt = document.body.innerText || '';
    const temConferencia = /confer/i.test(txt);
    const de = document.documentElement;
    return JSON.stringify({
        largura: window.innerWidth,
        gapCabecalhoCartao: Math.round(gap * 10) / 10,
        diffCentroSeletorTitulo: Math.round(diffCentro * 10) / 10,
        tituloCy: Math.round(T.cy * 10) / 10, seletorCy: Math.round(S.cy * 10) / 10,
        headerBottom: Math.round(H.bottom * 10) / 10, cartaoTop: Math.round(C.top * 10) / 10,
        seletorBottom: Math.round(S.bottom*10)/10, tituloBottom: Math.round(T.bottom*10)/10,
        seletorLeft: Math.round(S.left), tituloLeft: Math.round(T.left),
        seletorAcimaTitulo: S.bottom <= T.top + 1,
        temConferencia,
        scrollW: de.scrollWidth, clientW: de.clientWidth, overflow: de.scrollWidth - de.clientWidth
    });
})()`;

const { chrome, ...ctx1 } = abrirChrome(1440, 960, 9470);
const api = await conectar(9470);
try {
    // cria o Dono via /Account/Setup (fica logado)
    await api.ir(BASE + '/Account/Setup');
    await espera(600);
    const setup = await api.avaliar(`(() => {
        const f = [...document.forms].find(x => x.querySelector('input[name="Email"]'));
        if (!f) return 'sem-form';
        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        const put = (sel, v) => { const e = f.querySelector(sel); if (!e) return sel; set.call(e, v); e.dispatchEvent(new Event('input', {bubbles:true})); return null; };
        const falta = [put('input[name="NomeExibicao"]','Dono Teste'), put('input[name="Email"]','dono@teste.com'), put('input[name="Senha"]','***REMOVIDO***'), put('input[name="ConfirmarSenha"]','***REMOVIDO***')].filter(Boolean);
        if (falta.length) return 'faltando: ' + falta.join(',');
        f.submit(); return 'enviado';
    })()`);
    await espera(2500);
    console.log('setup dono:', String(setup), '-> url=', String(await api.avaliar('location.href')));

    console.log('\nlargura | tema | gap(hdr->cartao) | diffCentro | conferencia? | seletorAbaixoDoTitulo | overflowX');
    for (const tema of ['claro', 'escuro']) {
        for (const w of [1440, 1024, 390]) {
            await api.cmd('Emulation.setDeviceMetricsOverride', { width: w, height: 900, deviceScaleFactor: 1, mobile: w < 1024 });
            await api.ir(BASE + '/Dashboard');
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
            await api.ir(BASE + '/Dashboard');
            await espera(700);
            const m = JSON.parse(String(await api.avaliar(MEDIR)));
            console.log(`${String(w).padStart(6)} | ${tema.padEnd(6)} | gap=${m.gapCabecalhoCartao}px | dCentro=${m.diffCentroSeletorTitulo}px | confer=${m.temConferencia} | seletorAbaixo=${m.seletorAcimaTitulo} | overflowX=${m.overflowW !== undefined ? m.overflow : m.overflow}`);
        }
    }
} finally {
    fechar({ ws: api.ws, pid: chrome.pid, perfil: ctx1.perfil });
    chrome.kill();
}
process.exit(0);
