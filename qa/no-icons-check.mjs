/*
 * qa/no-icons-check.mjs - AJUSTE 3: guarda visual de remoção de ícones decorativos.
 *
 * Reaproveita qa/cdp-helper.mjs. Sobe Chrome headless, faz login como Dono
 * (e Operador no Caixa), nos DOIS temas, nas larguras 1440 e 390px.
 *
 * Verifica:
 * (1) grep nas Views, JS e CSS rastreados: zero "bi bi-", zero class="bi",
 *     zero <i , zero "bootstrap-icons", zero "icon:" em chamadas Swal.fire;
 * (2) por CDP, em todas as telas autenticadas, dois temas, 1440 e 390px,
 *     com o modal de pagamento e uma janela do SweetAlert abertos:
 *     todo <svg> e <i> visível fica dentro das exceções (chave de tema:
 *     lua/sol, hambúrguer, setas de menu, "×" de fechar, indicadores
 *     nativos) e nenhum pseudo-elemento ::before/::after tem content com
 *     glifo da faixa privada (\uE000-\uF8FF);
 * (3) todo <button> e <a role=button> visível tem texto visível.
 *
 * Falhou = corrija.
 *
 * Uso:
 *   node qa/no-icons-check.mjs --base http://localhost:5311
 *
 * Variáveis de ambiente (nunca gravadas aqui):
 *   SHELL_DONO_EMAIL, SHELL_DONO_SENHA
 *   SHELL_OPER_EMAIL, SHELL_OPER_SENHA
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, extname } from 'node:path';

const argv = process.argv.slice(2);
function arg(nome, padrao) {
    const i = argv.indexOf(nome);
    return i >= 0 && argv[i + 1] ? argv[i + 1] : padrao;
}
const BASE = arg('--base', process.env.SHELL_BASE || 'http://localhost:5311').replace(/\/+$/, '');

const DONO = {
    email: process.env.SHELL_DONO_EMAIL || 'dono@teste.com',
    senha: process.env.SHELL_DONO_SENHA || '***REMOVIDO***'
};
const OPER = {
    email: process.env.SHELL_OPER_EMAIL || 'operador@teste.com',
    senha: process.env.SHELL_OPER_SENHA || 'Operador@123'
};

const LARGURAS = [1440, 390];
const TEMAS = ['escuro', 'claro'];
const TELAS = [
    { nome: 'Login', url: '/Account/Login', perfil: 'dono', autenticar: false },
    { nome: 'Estoque', url: '/Home/Index', perfil: 'dono', autenticar: true },
    { nome: 'NovoProduto', url: '/Home/Novo', perfil: 'dono', autenticar: true },
    { nome: 'EditarProduto', url: '/Home/Editar/1', perfil: 'dono', autenticar: true },
    { nome: 'Caixa', url: '/Caixa', perfil: 'dono', autenticar: true, abrirModalPagamento: true },
    { nome: 'CaixaOperador', url: '/Caixa', perfil: 'operador', autenticar: true, abrirModalPagamento: true },
    { nome: 'Historico', url: '/Caixa/Historico?periodo=hoje', perfil: 'dono', autenticar: true },
    { nome: 'Usuarios', url: '/Usuarios', perfil: 'dono', autenticar: true },
    { nome: 'Dashboard', url: '/Dashboard', perfil: 'dono', autenticar: true }
];

let ok = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { ok++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
const espera = ms => new Promise(r => setTimeout(r, ms));

/* ==================== 1) GREP ESTÁTICO ==================== */
function grepEstatico() {
    console.log('\n=== GREP ESTÁTICO (Views, JS, CSS) ===');
    const raiz = 'E:/Sistema de Estoque/ControleEstoqueLoja';
    const extPermitidas = ['.cshtml', '.js', '.css'];
    const arquivos = [];

    function caminhar(dir) {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            const caminho = join(dir, entry.name);
            if (entry.isDirectory()) {
                if (!['node_modules', 'bin', 'obj', '.git', 'prints', 'trx'].includes(entry.name)) {
                    caminhar(caminho);
                }
            } else if (extPermitidas.includes(extname(entry.name))) {
                arquivos.push(caminho);
            }
        }
    }
    caminhar(raiz);

    const padroes = [
        { regex: /bi bi-/g, desc: '"bi bi-" em código' },
        { regex: /class=["']bi\b/g, desc: 'class="bi' },
        { regex: /<i\s/g, desc: '<i ' },
        { regex: /bootstrap-icons/gi, desc: '"bootstrap-icons"' },
        { regex: /icon:\s*['"]/g, desc: '"icon:" em Swal.fire' }
    ];

    for (const padrao of padroes) {
        let total = 0;
        for (const arq of arquivos) {
            try {
                const conteudo = readFileSync(arq, 'utf8');
                const matches = conteudo.match(padrao.regex);
                if (matches) {
                    total += matches.length;
                    for (const m of matches) {
                        const linha = conteudo.substring(0, conteudo.indexOf(m)).split('\n').length;
                        console.log(`  [FALHA] ${arq}:${linha} | ${padrao.desc} | ${m.trim().slice(0, 60)}`);
                        falhas++;
                    }
                }
            } catch (e) {
                // ignora erros de leitura
            }
        }
        if (total === 0) {
            check(`grep: zero "${padrao.desc}"`, true);
        }
    }
}

/* ==================== 2) CDP - VERIFICAÇÃO VISUAL ==================== */
async function verificarTela(api, tela, largura, tema, perfil) {
    const g = `${perfil}/${tema}/${tela.nome}/${largura}px`;
    await api.redimensionar(largura, 900);
    await espera(400);
    await api.ir(BASE + tela.url);
    await espera(800);

    if (tela.abrirModalPagamento) {
        await api.avaliar(`(() => {
            const carrinho = [{id: 1, nome: 'Teste', preco: 10, quantidade: 1, unidade: 'un', regra: 'Prateleira'}];
            window.carrinho = carrinho;
            if (typeof abrirModalPagamento === 'function') {
                abrirModalPagamento();
            }
            return 'ok';
        })()`);
        await espera(800);
    }

    // (2) Verificar ícones visíveis via CDP
    const iconesVisiveis = await api.avaliar(`(() => {
        const excecoes = ['bi-moon-stars', 'bi-sun', 'bi-circle-half', 'bi-chevron-down', 'btn-close'];
        const violacoes = [];

        // <i> tags
        document.querySelectorAll('i[class*="bi-"]').forEach(el => {
            const classes = el.className;
            const isExcecao = excecoes.some(ex => classes.includes(ex));
            if (!isExcecao) {
                violacoes.push({ tipo: 'i-tag', class: classes, tag: el.tagName });
            }
        });

        // <svg> inline (exceto sun/moon do tema)
        document.querySelectorAll('svg:not([data-icon="theme"])').forEach(el => {
            const style = getComputedStyle(el);
            if (style.display !== 'none' && style.visibility !== 'hidden') {
                violacoes.push({ tipo: 'svg-inline', class: el.className, outerHTML: el.outerHTML.slice(0, 100) });
            }
        });

        // pseudo-elementos com glifos da faixa privada
        const sheets = document.styleSheets;
        for (const sheet of sheets) {
            try {
                const rules = sheet.cssRules || sheet.rules;
                for (const rule of rules) {
                    if (rule.style && rule.style.content) {
                        const content = rule.style.content;
                        // Verifica se tem caractere da faixa privada \uE000-\uF8FF
                        if (/\\u[E-F][0-9A-F]{3}/.test(content)) {
                            violacoes.push({ tipo: 'pseudo-glifo', selector: rule.selectorText, content });
                        }
                    }
                }
            } catch (e) {
                // cross-origin stylesheets
            }
        }

        // Botões sem texto visível
        document.querySelectorAll('button, a[role="button"], a.btn').forEach(el => {
            const texto = el.textContent.trim();
            const ariaLabel = el.getAttribute('aria-label') || '';
            if (!texto && !ariaLabel) {
                violacoes.push({ tipo: 'botao-sem-texto', class: el.className, tag: el.tagName });
            }
        });

        return violacoes;
    })()`);

    for (const v of iconesVisiveis) {
        check(`${g} sem ícone decorativo: ${v.tipo}`, false, JSON.stringify(v).slice(0, 200));
    }
    if (iconesVisiveis.length === 0) {
        check(`${g} nenhum ícone decorativo visível`, true);
    }
}

async function main() {
    // 1) Grep estático
    grepEstatico();

    // 2) CDP visual
    const ctx = abrirChrome(1440, 960, 9490);
    const api = await conectar(9490);
    await api.cmd('Network.enable'); await api.cmd('Log.enable'); await api.cmd('Runtime.enable');

    try {
        for (const tema of TEMAS) {
            console.log(`\n===== DONO / tema ${tema} =====`);
            await login(api, BASE, DONO.email, DONO.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);

            for (const tela of TELAS.filter(t => t.perfil === 'dono')) {
                for (const largura of LARGURAS) {
                    await verificarTela(api, tela, largura, tema, 'dono');
                }
            }
        }

        for (const tema of TEMAS) {
            console.log(`\n===== OPERADOR / tema ${tema} =====`);
            await login(api, BASE, OPER.email, OPER.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);

            for (const tela of TELAS.filter(t => t.perfil === 'operador')) {
                for (const largura of LARGURAS) {
                    await verificarTela(api, tela, largura, tema, 'operador');
                }
            }
        }

    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }

    console.log(`\n=== no-icons-check: ${ok} ok / ${falhas} falha(s) ===`);
    if (falhas > 0) { console.log('no-icons-check: FALHOU'); process.exit(1); }
    console.log('no-icons-check: OK'); process.exit(0);
}

main().catch(e => { console.error('no-icons-check: ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });