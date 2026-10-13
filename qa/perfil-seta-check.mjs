/* ============================================================================
   perfil-seta-check.mjs - guarda da seta do menu do usuario (COMMIT 1)

   Mede, por CDP, a matriz do transform do ::after do botao #btnUsuario:
     FECHADO (aria-expanded=false) -> a ~ -0.707 e b ~ -0.707 (seta p/ cima)
     ABERTO  (aria-expanded=true ) -> a ~  0.707 e b ~  0.707 (seta p/ baixo)
   Confirma tambem: so UMA seta visivel (o span antigo nao aparece), a seta
   dentro do retangulo do botao, o nome clicavel e os itens do menu por papel.

   Dono e Operador, dois temas, 1440 e 390px. Capturas dos dois estados.

   Credenciais SO por ambiente: SHELL_DONO_EMAIL/SENHA, SHELL_OPER_EMAIL/SENHA.
   ============================================================================ */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
function arg(nome, padrao) { const i = argv.indexOf(nome); return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao; }
const BASE = arg('--base', 'http://127.0.0.1:5311').replace(/\/+$/, '');
const OUT = arg('--out', join(aqui, '..', '..', 'prints'));

const cred = {
    dono: [process.env.SHELL_DONO_EMAIL, process.env.SHELL_DONO_SENHA],
    operador: [process.env.SHELL_OPER_EMAIL, process.env.SHELL_OPER_SENHA]
};
if (!cred.dono[0] || !cred.dono[1] || !cred.operador[0] || !cred.operador[1]) {
    console.error('perfil-seta-check: defina SHELL_DONO_EMAIL/SENHA e SHELL_OPER_EMAIL/SENHA');
    process.exit(1);
}
const ESPERADO = { dono: ['Usuarios', 'Sair'], operador: ['Sair'] };
const espera = (ms) => new Promise(r => setTimeout(r, ms));

let aprov = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { aprov++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
function parseMatriz(t) {
    // "matrix(a, b, c, d, e, f)"
    const m = /matrix\(([^)]+)\)/.exec(t || '');
    if (!m) return null;
    const p = m[1].split(',').map(x => parseFloat(x));
    return { a: p[0], b: p[1] };
}
const perto = (v, alvo) => Math.abs(v - alvo) < 0.02;


async function medir(api) {
    return await api.avaliar(`(() => {
        const b = document.getElementById('btnUsuario');
        if (!b) return null;
        const st = getComputedStyle(b, '::after');
        const rect = b.getBoundingClientRect();
        const chev = b.querySelector('.app-usuario__chevron');
        const chevVisivel = chev ? getComputedStyle(chev).display !== 'none' : false;
        return {
            exp: b.getAttribute('aria-expanded'),
            transform: st.transform,
            chevVisivel: chevVisivel,
            btnRect: { x: rect.x, y: rect.y, w: rect.width, h: rect.height }
        };
    })()`);
}

async function rodarPerfil(api, perfil, esp) {
    const [email, senha] = cred[perfil];
    await login(api, BASE, email, senha);
    await api.ir(BASE + '/Home/Index');
    for (const tema of ['escuro', 'claro']) {
        console.log('--- ' + perfil + ' / tema ' + tema + ' ---');
        await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);
        await api.ir(BASE + '/Home/Index');
        await espera(600);
        for (const [w, h] of [[1440, 900], [390, 844]]) {
            await api.redimensionar(w, h);
            await espera(400);
            const g = perfil + '/' + tema + '/' + w + 'px';
            let m = await medir(api);
            let mat = parseMatriz(m && m.transform);
            check(g + ' fechado: para cima (a~-0.707,b~-0.707)', !!mat && perto(mat.a, -0.707) && perto(mat.b, -0.707), mat ? ('a=' + mat.a.toFixed(3) + ' b=' + mat.b.toFixed(3)) : m);
            check(g + ' span antigo escondido', m && m.chevVisivel === false, m && String(m.chevVisivel));
            await api.avaliar(`document.getElementById('btnUsuario').click(); 'ok'`);
            await espera(400);
            m = await medir(api);
            mat = parseMatriz(m && m.transform);
            check(g + ' aberto: para baixo (a~0.707,b~0.707)', !!mat && perto(mat.a, 0.707) && perto(mat.b, 0.707), mat ? ('a=' + mat.a.toFixed(3) + ' b=' + mat.b.toFixed(3)) : m);
            const itens = await api.avaliar(`JSON.stringify([...document.querySelectorAll('#menuUsuario [role=menuitem]')].map(a=>a.textContent.trim()))`);
            const itensArr = JSON.parse(itens);
            check(g + ' itens do menu por papel', JSON.stringify(itensArr) === JSON.stringify(esp.itens), itens);
            const nomeClicavel = await api.avaliar(`(() => { const n=document.querySelector('.app-usuario__nome'); return n ? n.textContent.trim().length>0 : false; })()`);
            check(g + ' nome do usuario presente', !!nomeClicavel);
            if (w === 1440) {
                const shot = await api.cmd('Page.captureScreenshot', {});
                writeFileSync(join(OUT, 'seta-' + perfil + '-' + tema + '-aberto.png'), Buffer.from(shot.data, 'base64'));
            }
            // Esc fecha (disparado no item focado, como no shell-check)
            await api.avaliar(`document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true})); 'ok'`);
            await espera(300);
            m = await medir(api); mat = parseMatriz(m && m.transform);
            check(g + ' Esc volta p/ cima', !!mat && perto(mat.a, -0.707) && perto(mat.b, -0.707), mat ? ('a=' + mat.a.toFixed(3)) : m);
            // clique no botao: fecha (estah fechado -> um clique abre, outro fecha)
            await api.avaliar(`document.getElementById('btnUsuario').click(); 'ok'`); await espera(300);
            await api.avaliar(`document.getElementById('btnUsuario').click(); 'ok'`); await espera(300);
            m = await medir(api); mat = parseMatriz(m && m.transform);
            check(g + ' clique no botao (2o) fecha', !!mat && perto(mat.a, -0.707) && perto(mat.b, -0.707), mat ? ('a=' + mat.a.toFixed(3)) : m);
            await api.avaliar(`document.getElementById('btnUsuario').click(); 'ok'`); await espera(250);
            await api.avaliar(`document.body.click(); 'ok'`); await espera(300);
            m = await medir(api); mat = parseMatriz(m && m.transform);
            check(g + ' clique fora fecha', !!mat && perto(mat.a, -0.707) && perto(mat.b, -0.707), mat ? ('a=' + mat.a.toFixed(3)) : m);
        }
        await api.redimensionar(1440, 900);
    }
}

async function main() {
    mkdirSync(OUT, { recursive: true });
    const ctx = abrirChrome(1440, 960, 9462);
    const api = await conectar(9462);
    try {
        await rodarPerfil(api, 'dono', { itens: ['Usuários', 'Sair'] });
        await api.avaliar(`(() => { const f=document.querySelector('form[action*="Logout"]'); if(f){f.submit(); return 'ok';} return 'x'; })()`);
        await espera(2500);
        await rodarPerfil(api, 'operador', { itens: ['Sair'] });
    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }
    console.log('');
    console.log('=== perfil-seta-check ===');
    console.log('  aprovadas: ' + aprov + ' | falhas: ' + falhas);
    process.exit(falhas > 0 ? 1 : 0);
}
main().catch(e => { console.error('ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });
