#!/usr/bin/env node
// qa/css-balance.mjs — guarda de balanco de chaves do CSS.
//
// Conta { e } de wwwroot/css/corporate-theme.css ignorando comentarios /* */
// e strings ("..." e '...'), rastreando a LINHA onde o saldo quebra. Node puro,
// sem npm. Saida: 0 = equilibrado; 1 = desbalanceado (motivo no stderr).
//
// Motivo: BUG-35 — uma chave "}" sobrando no fim do arquivo anulou 208 regras
// seguintes do tema e quebrou os clics da tela inteira. Este script e a rede
// que impede que isso volte sem alguem perceber.
//
// Uso: node qa/css-balance.mjs   (ou via caminho absoluto; o alvo e resolvido
// a partir da pasta do proprio script: ../wwwroot/css/corporate-theme.css)
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const minhaPasta = dirname(fileURLToPath(import.meta.url));
const alvo = join(minhaPasta, '..', 'wwwroot', 'css', 'corporate-theme.css');

let css;
try {
  css = readFileSync(alvo, 'utf8');
} catch (erro) {
  console.error(`css-balance: FALHA ao ler ${alvo}: ${erro.message}`);
  process.exit(1);
}

let saldo = 0;          // { abertos menos } fechados
let linha = 1;          // linha atual (1-based)
let estado = 'codigo';  // codigo | string | comentario
let aspas = '';
const pilha = [];       // linhas das { ainda abertas
let problema = null;    // primeira anormalidade, em ordem de leitura
let pares = 0;

for (let i = 0; i < css.length; i++) {
  const c = css[i];
  const prox = css[i + 1];

  if (estado === 'comentario') {
    if (c === '*' && prox === '/') { estado = 'codigo'; i++; continue; }
    if (c === '\n') linha++;
    continue;
  }

  if (estado === 'string') {
    if (c === '\\') { i++; continue; }            // escape: proximo char e literal
    if (c === '\n') { estado = 'codigo'; linha++; continue; } // string nao fechada: ressincroniza
    if (c === aspas) { estado = 'codigo'; continue; }
    continue;
  }

  // estado === 'codigo'
  if (c === '/' && prox === '*') { estado = 'comentario'; i++; continue; }
  if (c === '"' || c === "'") { estado = 'string'; aspas = c; continue; }
  if (c === '\n') { linha++; continue; }
  if (c === '{') { saldo++; pares++; pilha.push(linha); continue; }
  if (c === '}') {
    saldo--;
    pilha.pop();
    if (saldo < 0 && problema === null) {
      problema = `chave "}" extra na linha ${linha} (o saldo fica ${saldo})`;
    }
    continue;
  }
}

if (problema !== null) {
  console.error(`css-balance: DESBALANCEADO — ${problema}`);
  process.exit(1);
}

if (saldo > 0) {
  const linhas = pilha.join(', ');
  console.error(
    `css-balance: DESBALANCEADO — ${saldo} chave(s) "{" sobrando no fim; ` +
    `aberta(s) na linha ${linhas}`);
  process.exit(1);
}

if (estado === 'comentario') {
  console.error('css-balance: DESBALANCEADO — comentario "/*" nunca fechado no fim do arquivo');
  process.exit(1);
}
if (estado === 'string') {
  console.error(`css-balance: DESBALANCEADO — string com aspas ${aspas} nunca fechada (linha ${linha})`);
  process.exit(1);
}

console.log(`css-balance: OK — ${pares} par(es) de chaves equilibrado(s) em ${alvo}`);
process.exit(0);
