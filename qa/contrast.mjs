/*
 * qa/contrast.mjs — PASSO 3 do redesign: guarda de contraste dos tokens.
 * Le os blocos de paleta de corporate-theme.css (escura, clara e a clara do
 * prefers-color-scheme), resolve os valores var()/color-mix() declarados e
 * cobra a RAZAO WCAG exata de cada par texto/fundo:
 *   4.5:1 — texto (WCAG AA)
 *   3.0:1 — borda/marca de interface (WCAG AA nao-texto)
 * Sai com codigo 1 se algum par ficar abaixo do minimo.
 */
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const aqui = dirname(fileURLToPath(import.meta.url));
const caminho = join(aqui, '..', 'wwwroot', 'css', 'corporate-theme.css');
const css = readFileSync(caminho, 'utf8');

function bloco(seletor) {
    const m = seletor.exec(css);
    if (!m) throw new Error('bloco de paleta nao encontrado: ' + seletor);
    const abre = css.indexOf('{', m.index);
    const fecha = css.indexOf('}', abre);
    const tokens = {};
    for (const t of css.slice(abre + 1, fecha).matchAll(/--([\w-]+):\s*([^;]+);/g)) {
        tokens[t[1]] = t[2].trim();
    }
    return tokens;
}

const escuro = bloco(/:root,\s*:root\[data-theme="dark"\]\s*\{/);
const claro = bloco(/:root\[data-theme="light"\]\s*\{/);
const auto = bloco(/:root:not\(\[data-theme\]\)\s*\{/);
const comum = bloco(/:root\s*\{/);

function rgb(hex) {
    const h = hex.replace('#', '');
    return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16));
}

/* resolvedor preguiçoso: hex | var(--x) | color-mix(in srgb, var(--a) N%, var(--b)) */
function novoResolvedor(base, escolha) {
    const mapa = { ...base, ...escolha };
    const cache = new Map();
    function res(nome) {
        const chave = nome.startsWith('--') ? nome.slice(2) : nome;
        if (cache.has(chave)) return cache.get(chave);
        const bruto = mapa[chave];
        if (bruto === undefined) throw new Error('token ausente: --' + chave);
        let out;
        if (bruto.startsWith('#')) {
            out = rgb(bruto);
        } else if (bruto.startsWith('var(')) {
            out = res(bruto.slice(4, bruto.length - 1).trim());
        } else {
            const mm = /^color-mix\(in srgb,\s*var\(--([\w-]+)\)\s*([\d.]+)%\s*,\s*var\(--([\w-]+)\)\s*\)$/.exec(bruto);
            if (!mm) throw new Error('valor nao resolvivel em --' + chave + ': ' + bruto);
            const a = res(mm[1]);
            const b = res(mm[3]);
            const p = Number(mm[2]) / 100;
            out = a.map((v, i) => Math.round(v * p + b[i] * (1 - p)));
        }
        cache.set(chave, out);
        return out;
    }
    return res;
}

/* WCAG 2.x: luminancia relativa e razao de contraste exata */
function canal(c) {
    const x = c / 255;
    return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
}
function lum([r, g, b]) {
    return 0.2126 * canal(r) + 0.7152 * canal(g) + 0.0722 * canal(b);
}
function razao(a, b) {
    const la = lum(a);
    const lb = lum(b);
    const alto = Math.max(la, lb);
    const baixo = Math.min(la, lb);
    return (alto + 0.05) / (baixo + 0.05);
}

/* Pares obrigatorios, iguais nas duas paletas (os tokens variam, a cobranca nao) */
function montarPares(res) {
    const pares = [];
    const texto = (desc, fg, bg, min = 4.5) => pares.push({
        desc,
        fg: typeof fg === 'string' ? res(fg) : fg,
        bg: typeof bg === 'string' ? res(bg) : bg,
        min
    });

    texto('texto / pagina', 'text', 'bg-page');
    texto('texto / cartao', 'text', 'bg-card');
    texto('texto / fundo alternado', 'text', 'bg-alt');
    texto('texto / campo de entrada', 'text', 'bg-input');
    texto('texto suave / pagina', 'text-soft', 'bg-page');
    texto('texto suave / cartao', 'text-soft', 'bg-card');
    texto('texto suave / fundo alternado', 'text-soft', 'bg-alt');
    texto('texto suave / barra lateral', 'text-soft', 'bg-sidebar');
    texto('link azul / cartao', 't-blue', 'bg-card');
    texto('link azul / pagina', 't-blue', 'bg-page');
    texto('item ativo / fundo ativo', 't-blue', 'bg-ativo');
    texto('selo azul (tinta 14%)', 't-blue', 's-blue-fundo');
    texto('selo violeta (tinta 14%)', 't-violet', 's-violet-fundo');
    texto('selo ciano (tinta 14%)', 't-cyan', 's-cyan-fundo');
    texto('selo verde (tinta 14%)', 't-green', 's-green-fundo');
    texto('selo ambar (tinta 14%)', 't-amber', 's-amber-fundo');
    texto('selo coral (tinta 14%)', 't-coral', 's-coral-fundo');
    texto('ink solido / acento solido (Swal e pilula)', 'accent-ink', 'accent');
    texto('botao primario (ink/fundo)', 'btn-primary-ink', 'btn-primary-bg');
    texto('botao neutro (ink/fundo)', 'btn-neutral-ink', 'btn-neutral-bg');
    texto('botao sucesso (ink/fundo)', 'btn-success-ink', 'btn-success-bg');
    texto('botao aviso (ink/fundo)', 'btn-warning-ink', 'btn-warning-bg');
    texto('botao info (ink/fundo)', 'btn-info-ink', 'btn-info-bg');
    texto('botao perigo (ink/fundo)', 'btn-danger-ink', 'btn-danger-bg');
    texto('valor verde / cartao', 'success', 'bg-card');
    texto('valor ciano / cartao', 'info', 'bg-card');
    texto('valor coral / cartao', 'danger', 'bg-card');
    texto('valor ambar / cartao', 't-amber', 'bg-card');
    texto('borda de acao primaria / cartao', 'btn-primary-bd', 'bg-card', 3);
    /* AJUSTE DE SHELL - icone sob o knob da chave de tema: tinta de alto
       contraste (--knob-ink) sobre o proprio knob (fundo --c-blue). Marcador
       de interface nao-texto, exige 3:1 nas DUAS paletas. */
    texto('icone sob o knob / fundo do knob', 'knob-ink', 'c-blue', 3);
    return pares;
}

let problemas = 0;
let total = 0;

for (const [nome, paleta] of [['escura', escuro], ['clara', claro]]) {
    const res = novoResolvedor(comum, paleta);
    const pares = montarPares(res);
    let falhas = 0;
    for (const p of pares) {
        total++;
        const r = razao(p.fg, p.bg);
        if (r < p.min) {
            falhas++;
            problemas++;
            console.log(`PROBLEMA [${nome}] ${p.desc}: razao ${r.toFixed(2)} < minimo ${p.min}`);
        }
    }
    console.log(`contrast: paleta ${nome} - ${pares.length} par(es), ${falhas} problema(s)`);
}

/* O @media (prefers-color-scheme: light) tem que ser um espelho EXATO do
   data-theme="light": se um dos dois mudar sozinho, o "auto" diverge do
   "claro" explicito e o usuario vê dois temas diferentes sem querer. */
const divergentes = [];
for (const k of new Set([...Object.keys(auto), ...Object.keys(claro)])) {
    if (auto[k] !== claro[k]) divergentes.push('--' + k);
}
if (divergentes.length > 0) {
    problemas++;
    console.log('PROBLEMA prefers-color-scheme difere de data-theme="light": ' + divergentes.join(', '));
}

if (problemas > 0) {
    console.log(`contrast: FALHOU - ${problemas} problema(s) em ${total} par(es) em corporate-theme.css`);
    process.exit(1);
}
console.log(`contrast: OK - ${total} par(es) verificados, 0 problema(s); auto = claro`);