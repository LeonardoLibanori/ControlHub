import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
const BASE = 'http://localhost:5311';
const DONO_EMAIL = process.env.SHELL_DONO_EMAIL || 'admin@loja.com';
const DONO_SENHA = process.env.SHELL_DONO_SENHA || 'Admin123';
const largura = Number(process.argv[2] || '390');
const periodo = process.argv[3] || 'hoje';
const { chrome, perfil, porta } = abrirChrome(largura, 900, 9477);
const api = await conectar(porta);
try {
  await login(api, BASE, DONO_EMAIL, DONO_SENHA);
  await api.cmd('Emulation.setDeviceMetricsOverride', { width: largura, height: 900, deviceScaleFactor: 1, mobile: false });
  await api.ir(BASE + '/Dashboard?periodo=' + encodeURIComponent(periodo));
  await new Promise(r => setTimeout(r, 1200));
  const res = await api.avaliar(`(() => {
    const cards = Array.from(document.querySelectorAll('.dsh-kpi-card'));
    const out = cards.map((c, i) => {
      const r = c.getBoundingClientRect();
      const pts = c.querySelectorAll('.dsh-kpi-ponto');
      const pt = pts.length ? getComputedStyle(pts[0]).backgroundColor : null;
      return { i: i+1, w: Math.round(r.width), h: Math.round(r.height), pt, html: c.outerHTML.slice(0, 300) };
    });
    return { width: window.innerWidth, cards: out };
  })()`);
  console.log(JSON.stringify(res, null, 2));
} catch (e) {
  console.error('ERRO', e.message);
} finally {
  fechar({ ws: api.ws, pid: chrome.pid, perfil });
  chrome.kill();
}
process.exit(0);