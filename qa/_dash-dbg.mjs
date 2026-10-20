import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
const BASE = 'http://localhost:5311';
const DONO_EMAIL = process.env.SHELL_DONO_EMAIL || 'admin@loja.com';
const DONO_SENHA = process.env.SHELL_DONO_SENHA || 'Admin123';
const periodo = process.argv[2] || 'ano';
const { chrome, perfil, porta } = abrirChrome(1440, 960, 9471);
const api = await conectar(porta);
try {
  await login(api, BASE, DONO_EMAIL, DONO_SENHA);
  await api.cmd('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
  await api.ir(BASE + '/Dashboard?periodo=' + encodeURIComponent(periodo));
  await new Promise(r => setTimeout(r, 1200));
  const res = await api.avaliar(`(() => {
    const q = sel => {
      const e = document.querySelector(sel);
      return e ? { existente: true, tag: e.tagName, cls: (e.className || '').toString(), txt: e.textContent.trim() } : { existente: false };
    };
    const boas = document.querySelector('.dsh-boas-vindas');
    const saud = document.querySelector('h1.dsh-saudacao');
    const cab = document.querySelector('.dsh-cabecalho');
    const secao = document.querySelector('.dsh-kpi-grade');
    const svg = document.querySelector('svg');
    return {
      url: location.href,
      boas: q('.dsh-boas-vindas'),
      cabecalho: cab ? { cls: cab.className, childCount: cab.children.length } : null,
      saudacao: q('h1.dsh-saudacao'),
      kpiGrade: !!secao,
      svg: svg ? { tag: svg.tagName, cls: (svg.className||'').toString() } : null,
      bodyText: document.body ? document.body.textContent.slice(0, 500) : '',
      htmlLen: document.documentElement.outerHTML.length
    };
  })()`);
  console.log(JSON.stringify(res, null, 2));
} catch (e) {
  console.error('ERRO', e.message);
  const s = await api.avaliar('document.body ? document.body.outerHTML.slice(0, 800) : String(e)');
  console.log('BODY-SNAP', s);
} finally {
  fechar({ ws: api.ws, pid: chrome.pid, perfil });
  chrome.kill();
}
process.exit(0);
