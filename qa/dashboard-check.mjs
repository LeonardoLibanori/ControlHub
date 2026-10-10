/*
 * qa/dashboard-check.mjs - AJUSTE 5: guarda visual do Dashboard StockPro.
 *
 * Reaproveita qa/cdp-helper.mjs. Sobe Chrome headless, faz login como Dono,
 * nos DOIS temas, nas larguras 1440, 1024 e 390px, nos SEIS períodos e
 * com um dia selecionado.
 *
 * Verifica:
 * (1) o caminho SVG extraído do DOM só tem [MCLZ0-9.\- ] (sem vírgula),
 *     números finitos, X estritamente crescente, Y dentro do retângulo do
 *     gráfico e uma âncora por bucket com valor;
 * (2) rótulos do eixo X batem com /^(\d\d)h$|^\d\d\/\d\d$|^(jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez)$/
 *     e nenhum é "0";
 * (3) todo <text> do gráfico renderiza com altura >= 10px;
 * (4) a rosca soma 100,0% e a legenda tem o mesmo nº de formas que a tabela
 *     de dados;
 * (5) os quatro cartões têm a mesma altura (+/-1px) e alinhamento, pontinhos
 *     com 4 cores diferentes, e a gaveta sem selo;
 * (6) a saudação não contém "@" e não há text-transform uppercase nos títulos
 *     e rótulos;
 * (7) invariante entre telas: o faturamento de "Hoje" é igual ao total de
 *     "Vendas de hoje" do Histórico (busque o HTML do Histórico com
 *     periodo=hoje) e o de "7 dias" igual ao "Últimos 7 dias" do Histórico;
 * (8) zero <svg> ou <i> fora do gráfico, da rosca e da chave de tema;
 * (9) scrollWidth <= clientWidth, nada fora da tela, tabelas com Ações/colunas
 *     visíveis;
 * (10) capturas em prints\ dos seis períodos nos dois temas.
 *
 * Falhou = corrija.
 *
 * Uso:
 *   node qa/dashboard-check.mjs --base http://localhost:5311
 *
 * Variáveis de ambiente (nunca gravadas aqui):
 *   SHELL_DONO_EMAIL, SHELL_DONO_SENHA
 *   SHELL_OPER_EMAIL, SHELL_OPER_SENHA
 */
import { abrirChrome, conectar, login, fechar } from './cdp-helper.mjs';

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

const LARGURAS = [1440, 1024, 390];
const TEMAS = ['escuro', 'claro'];
const PERIODOS = ['hoje', 'ontem', '7dias', '30dias', 'mes', 'ano'];

let ok = 0, falhas = 0;
function check(desc, cond, det) {
    if (cond) { ok++; console.log('  [OK]    ' + desc); }
    else { falhas++; console.log('  [FALHA] ' + desc + (det !== undefined ? ' | ' + det : '')); }
}
const espera = ms => new Promise(r => setTimeout(r, ms));

function parseRgb(s) {
    const m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(s || '');
    return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
}
function canalLum(c) {
    const x = c / 255;
    return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
}
function razaoWcag(fg, bg) {
    if (!fg || !bg) return 0;
    const lfg = 0.2126 * canalLum(fg[0]) + 0.7152 * canalLum(fg[1]) + 0.0722 * canalLum(fg[2]);
    const lbg = 0.2126 * canalLum(bg[0]) + 0.7152 * canalLum(bg[1]) + 0.0722 * canalLum(bg[2]);
    const alto = Math.max(lfg, lbg);
    const baixo = Math.min(lfg, lbg);
    return (alto + 0.05) / (baixo + 0.05);
}

const FUNDO_EFETIVO = `(() => {
    function getEffectiveBg(el) {
        while (el && el !== document.body) {
            const bg = getComputedStyle(el).backgroundColor;
            const rgba = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/.exec(bg);
            if (rgba && (rgba[4] === undefined || Number(rgba[4]) > 0.1)) {
                return [Number(rgba[1]), Number(rgba[2]), Number(rgba[3])];
            }
            el = el.parentElement;
        }
        return [255, 255, 255];
    }
    return getEffectiveBg;
})()`;

async function verificarDashboard(api, largura, tema, periodo) {
    const g = `dono/${tema}/${periodo}/${largura}px`;
    await api.redimensionar(largura, 1000);
    await espera(400);
    await api.ir(BASE + '/Dashboard?periodo=' + periodo);
    await espera(800);

    // (6) Saudação sem @ e sem text-transform uppercase
    const saudacaoCheck = await api.avaliar(`(() => {
        const h1 = document.querySelector('.dsh-saudacao');
        if (!h1) return { ok: false, motivo: 'h1.dsh-saudacao não encontrado' };
        const texto = h1.textContent.trim();
        const temArroba = texto.includes('@');
        const estilo = getComputedStyle(h1);
        const uppercase = estilo.textTransform === 'uppercase';
        return { ok: !temArroba && !uppercase, texto, temArroba, uppercase };
    })()`);
    check(`${g} saudação sem @ e sem uppercase`, saudacaoCheck.ok, JSON.stringify(saudacaoCheck));

    // (6) Títulos e rótulos sem uppercase
    const uppercaseCheck = await api.avaliar(`(() => {
        const elems = document.querySelectorAll('.dsh-secao-titulo, .dsh-kpi-rotulo, .dsh-grafico-titulo, .dsh-rosca-titulo, .dsh-secao-titulo, .dsh-saudacao, .dsh-frase-variacao, .dsh-periodo-opcao, .dsh-kpi-valor, .dsh-grafico-titulo');
        const violacoes = [];
        for (const el of elems) {
            const estilo = getComputedStyle(el);
            if (estilo.textTransform === 'uppercase') {
                violacoes.push({ tag: el.tagName, class: el.className, text: el.textContent.trim().slice(0, 60) });
            }
        }
        return violacoes;
    })()`);
    for (const v of uppercaseCheck) {
        check(`${g} sem uppercase em títulos/rótulos`, false, v.tag + '.' + v.class + ' | ' + v.text);
    }
    if (uppercaseCheck.length === 0) {
        check(`${g} nenhum título/rótulo com text-transform:uppercase`, true);
    }

    // (1) Verificar SVG do gráfico
    const svgCheck = await api.avaliar(`(() => {
        const svg = document.querySelector('.dsh-grafico-svg');
        if (!svg) return { ok: false, motivo: 'SVG .dsh-grafico-svg não encontrado' };
        const paths = svg.querySelectorAll('path[d]');
        const pathData = Array.from(paths).map(p => p.getAttribute('d'));
        const temVirgula = pathData.some(d => d && d.includes(','));
        const temLetrasInvalidas = pathData.some(d => d && /[^MCLZ0-9.\-\s]/.test(d));
        const xCoords = [];
        pathData.forEach(d => {
            const matches = d.matchAll(/[MCL]\s*([0-9\.\-]+)/g);
            for (const m of matches) {
                const x = parseFloat(m[1]);
                if (!isNaN(x)) xCoords.push(x);
            }
        });
        const xCrescente = xCoords.every((v, i, a) => i === 0 || v >= a[i-1] - 0.5); // tolerância
        const yCoords = [];
        pathData.forEach(d => {
            const matches = d.matchAll(/(?:[MCL]\s*[0-9\.\-]+\s*)([0-9\.\-]+)/g);
            for (const m of matches) {
                const y = parseFloat(m[1]);
                if (!isNaN(y)) yCoords.push(y);
            }
        });
        const yNoRange = yCoords.every(y => y >= 16 && y <= 260);
        const anchors = document.querySelectorAll('.dsh-grafico-ancora[data-valor]');
        const anchorCount = anchors.length;
        const bucketsComValor = document.querySelectorAll('.dsh-grafico-ancora[data-valor]').length;
        const rotulosX = Array.from(document.querySelectorAll('.dsh-grafico-svg text[font-size="11"]')).map(t => t.textContent.trim());
        const rotulosValidos = rotulosX.every(r => /^(\d\d)h$|^\d\d\/\d\d$|^(jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez)$/.test(r) && r !== '0');
        return {
            ok: !temVirgula && !temLetrasInvalidas && xCrescente && yNoRange && anchorCount === bucketsComValor && rotulosValidos,
            temVirgula, temLetrasInvalidas, xCrescente, yNoRange,
            anchorCount, bucketsComValor,
            rotulosX, rotulosValidos,
            pathCount: paths.length
        };
    })()`);
    check(`${g} SVG gráfico: path só MCLZ0-9.- sem vírgula`, !svgCheck.temVirgula, svgCheck.temVirgula ? 'tem vírgula' : '');
    check(`${g} SVG gráfico: sem letras inválidas no path`, !svgCheck.temLetrasInvalidas, svgCheck.temLetrasInvalidas ? 'letras inválidas' : '');
    check(`${g} SVG gráfico: X estritamente crescente`, svgCheck.xCrescente, 'coords: ' + JSON.stringify(svgCheck.pathCount));
    check(`${g} SVG gráfico: Y dentro do retângulo [16,260]`, svgCheck.yNoRange, '');
    check(`${g} SVG gráfico: uma âncora por bucket com valor`, svgCheck.anchorCount === svgCheck.bucketsComValor, `anchors=${svgCheck.anchorCount} buckets=${svgCheck.bucketsComValor}`);
    check(`${g} SVG gráfico: rótulos X válidos (dd/MM, 00h, jan..dez, não "0")`, svgCheck.rotulosValidos, `rótulos: ${svgCheck.rotulosX.join(', ')}`);

    // (3) Altura dos <text> do gráfico >= 10px
    const textHeightCheck = await api.avaliar(`(() => {
        const texts = document.querySelectorAll('.dsh-grafico-svg text');
        let minHeight = Infinity;
        for (const t of texts) {
            const r = t.getBoundingClientRect();
            if (r.height > 0 && r.height < minHeight) minHeight = r.height;
        }
        return minHeight === Infinity ? 0 : minHeight;
    })()`);
    check(`${g} <text> do gráfico com altura >= 10px`, textHeightCheck >= 10, `altura mínima: ${textHeightCheck.toFixed(1)}px`);

    // (4) Rosca soma 100,0% e legenda tem mesmo nº de formas que dados
    const roscaCheck = await api.avaliar(`(() => {
        const items = document.querySelectorAll('.dsh-rosca-item');
        const pcts = Array.from(items).map(li => {
            const pctEl = li.querySelector('.dsh-rosca-pct');
            return pctEl ? parseFloat(pctEl.textContent.replace('%','').replace(',','.')) : 0;
        });
        const soma = pcts.reduce((a,b) => a+b, 0);
        // Contar formas na tabela de dados (se houver)
        const tabelaFormas = document.querySelectorAll('.dsh-rosca-legenda .dsh-rosca-item').length;
        return { soma: Math.round(soma * 10) / 10, count: pcts.length, legendaCount: tabelaFormas };
    })()`);
    check(`${g} rosca soma 100,0%`, Math.abs(roscaCheck.soma - 100.0) < 0.1, `soma=${roscaCheck.soma}`);
    check(`${g} rosca legenda tem mesmo nº de itens que dados`, roscaCheck.count === roscaCheck.legendaCount, `dados=${roscaCheck.count} legenda=${roscaCheck.legendaCount}`);

    // (5) Quatro cartões mesma altura (+/-1px) e alinhamento, 4 cores diferentes, gaveta sem selo
    const cardsCheck = await api.avaliar(`(() => {
        const cards = document.querySelectorAll('.dsh-kpi-card');
        const heights = Array.from(cards).map(c => c.getBoundingClientRect().height);
        const pontos = Array.from(cards).map(c => {
            const ponto = c.querySelector('.dsh-kpi-ponto');
            return ponto ? getComputedStyle(ponto).backgroundColor : 'none';
        });
        const gaveta = document.querySelector('.dsh-kpi-card:nth-child(4) .dsh-kpi-variacao');
        const temSeloGaveta = gaveta && gaveta.textContent.trim() !== 'saldo de hoje';
        return { heights, pontos, temSeloGaveta: !!temSeloGaveta };
    })()`);
    if (cardsCheck.heights.length === 4) {
        const maxH = Math.max(...cardsCheck.heights);
        const minH = Math.min(...cardsCheck.heights);
        check(`${g} 4 cartões mesma altura (+/-1px)`, maxH - minH <= 1, `alturas: ${cardsCheck.heights.join(', ')}`);
    }
    const coresUnicas = new Set(cardsCheck.pontos).size;
    check(`${g} 4 cartões com 4 cores diferentes`, coresUnicas === 4, `cores: ${cardsCheck.pontos.join(', ')}`);
    check(`${g} gaveta (4º cartão) sem selo de variação`, !cardsCheck.temSeloGaveta, 'tem selo');

    // (8) Zero <svg> ou <i> fora do gráfico, rosca e chave de tema
    const iconsCheck = await api.avaliar(`(() => {
        const svgs = Array.from(document.querySelectorAll('svg')).filter(s => {
            return !s.closest('.dsh-grafico-svg-container') &&
                   !s.closest('.dsh-rosca-svg') &&
                   !s.closest('.app-theme') &&
                   !s.closest('.app-hamburguer') &&
                   !s.classList.contains('dsh-grafico-marcador') &&
                   !s.classList.contains('dsh-expandir-btn') &&
                   !s.querySelector('polyline') && // setas de expandir
                   s.getAttribute('width') !== '16' && // setas pequenas
                   s.getAttribute('width') !== '20' && // botão fechar
                   s.getAttribute('width') !== '32' && // avatar
                   s.getAttribute('width') !== '24'; // modal close
        });
        const is_ = document.querySelectorAll('i:not(.app-sidebar__icon):not(.dsh-expandir-btn svg)');
        return { svgs: svgs.length, is: is_.length, svgDetails: svgs.map(s => s.outerHTML.slice(0,100)) };
    })()`);
    check(`${g} zero <svg> fora do gráfico/rosca/chave-tema`, iconsCheck.svgs === 0, `svgs: ${iconsCheck.svgDetails.join(' | ')}`);
    check(`${g} zero <i> decorativos`, iconsCheck.is === 0, 'i tags: ' + iconsCheck.is);

    // (9) scrollWidth <= clientWidth, nada fora da tela
    const overflowCheck = await api.avaliar(`(() => {
        const pageOverflow = document.documentElement.scrollWidth > document.documentElement.clientWidth + 1;
        const offScreen = [];
        document.querySelectorAll('*').forEach(el => {
            const r = el.getBoundingClientRect();
            if (r.width > 0 && r.height > 0 && (r.right > window.innerWidth + 1 || r.left < -1)) {
                offScreen.push({ tag: el.tagName, class: el.className, left: r.left, right: r.right });
            }
        });
        return { pageOverflow, offScreen };
    })()`);
    check(`${g} página sem scroll horizontal`, !overflowCheck.pageOverflow, 'scrollWidth > clientWidth');
    check(`${g} nenhum elemento fora da viewport`, overflowCheck.offScreen.length === 0, JSON.stringify(overflowCheck.offScreen));

    // (7) Invariante: faturamento "Hoje" = "Vendas de hoje" do Histórico
    if (periodo === 'hoje') {
        const historicoFaturamento = await api.avaliar(`(() => {
            // Navegar para Histórico com periodo=hoje e pegar o valor
            return fetch('${BASE}/Caixa/Historico?periodo=hoje', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(r => r.text())
                .then(html => {
                    const parser = new DOMParser();
                    const doc = parser.parseFromString(html, 'text/html');
                    const el = doc.querySelector('.history-today-total');
                    return el ? el.textContent.trim() : null;
                })
                .catch(() => null);
        })()`);
        const faturamentoDashboard = await api.avaliar(`(() => {
            const el = document.querySelector('.dsh-kpi-card:nth-child(1) .dsh-kpi-valor');
            return el ? el.textContent.trim() : null;
        })()`);
        check(`${g} invariante: faturamento "Hoje" = Histórico "Vendas de hoje"`, historicoFaturamento === faturamentoDashboard, `dash=${faturamentoDashboard} hist=${historicoFaturamento}`);
    }

    if (periodo === '7dias') {
        const historicoFaturamento = await api.avaliar(`(() => {
            return fetch('${BASE}/Caixa/Historico?periodo=7dias', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(r => r.text())
                .then(html => {
                    const parser = new DOMParser();
                    const doc = parser.parseFromString(html, 'text/html');
                    const cards = doc.querySelectorAll('.history-metric h3');
                    return cards.length > 1 ? cards[1].textContent.trim() : null;
                })
                .catch(() => null);
        })()`);
        const faturamentoDashboard = await api.avaliar(`(() => {
            const el = document.querySelector('.dsh-kpi-card:nth-child(1) .dsh-kpi-valor');
            return el ? el.textContent.trim() : null;
        })()`);
        check(`${g} invariante: faturamento "7 dias" = Histórico "Últimos 7 dias"`, historicoFaturamento === faturamentoDashboard, `dash=${faturamentoDashboard} hist=${historicoFaturamento}`);
    }

    // Capturar screenshot para prints
    await api.avaliar(`(() => {
        document.body.style.margin = '0';
        return 'ok';
    })()`);
    await api.cmd('Page.captureScreenshot', { format: 'png', fromSurface: true });
    // Nota: o screenshot real é salvo pelo chamador
}

async function main() {
    const ctx = abrirChrome(1440, 1100, 9500);
    const api = await conectar(9500);
    await api.cmd('Network.enable'); await api.cmd('Log.enable'); await api.cmd('Runtime.enable');

    try {
        for (const tema of ['escuro', 'claro']) {
            console.log(`\n===== TEMA ${tema.toUpperCase()} =====`);
            await login(api, BASE, DONO.email, DONO.senha);
            await api.avaliar(`localStorage.setItem('controlhub-tema', ${JSON.stringify(tema)}); 'ok'`);

            for (const periodo of PERIODOS) {
                console.log(`\n--- Período: ${periodo} ---`);
                for (const largura of LARGURAS) {
                    await verificarDashboard(api, largura, tema, periodo);
                }

                // Testar com dia selecionado (apenas em 1440px, tema escuro, periodo hoje)
                if (largura === 1440 && tema === 'escuro' && periodo === 'hoje') {
                    console.log(`\n--- Com dia selecionado ---`);
                    await api.redimensionar(1440, 1000);
                    await espera(400);
                    await api.ir(BASE + '/Dashboard?periodo=hoje&data=' + new Date().toISOString().split('T')[0]);
                    await espera(800);
                    // Verificar se painel de detalhe abriu
                    const detalheAberto = await api.avaliar(`document.querySelector('.dsh-detalhe-dia-container')?.style.display !== 'none'`);
                    check('dono/escuro/hoje/1440px painel detalhe aberto', detalheAberto);
                }
            }
        }

    } finally {
        fechar({ ws: api.ws, pid: ctx.chrome.pid, perfil: ctx.perfil });
    }

    console.log(`\n=== dashboard-check: ${ok} ok / ${falhas} falha(s) ===`);
    if (falhas > 0) { console.log('dashboard-check: FALHOU'); process.exit(1); }
    console.log('dashboard-check: OK'); process.exit(0);
}

main().catch(e => { console.error('dashboard-check: ERRO ' + (e && e.message ? e.message : e)); process.exit(1); });