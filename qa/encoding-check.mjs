// qa/encoding-check.mjs — guarda de UTF-8 dos arquivos versionados (PARTE D).
//
// Para cada arquivo rastreado (git ls-files) com extensao .cshtml, .cs, .js,
// .css, .md, .json, .ps1 ou .mjs:
//   1. decodifica os bytes com TextDecoder('utf-8', { fatal: true });
//   2. se o decoder falhar -> o arquivo NAO e UTF-8 valido (lista a linha);
//   3. se passar, procura U+FFFD literal no texto (lista a linha e o trecho).
// Qualquer problema -> exit 1. Sem problemas -> exit 0.
//
// Uso: node qa/encoding-check.mjs   (na raiz do repo)

import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const EXTENSOES = new Set(['.cshtml', '.cs', '.js', '.css', '.md', '.json', '.ps1', '.mjs']);
const raiz = path.dirname(path.dirname(fileURLToPath(import.meta.url)));

const arquivos = execFileSync('git', ['ls-files', '-z'], { cwd: raiz, maxBuffer: 64 * 1024 * 1024 })
  .toString('utf-8')
  .split('\0')
  .filter((caminho) => caminho && EXTENSOES.has(path.extname(caminho).toLowerCase()));

const problemas = [];

function linhasCom(str, agulha) {
  const saida = [];
  const linhas = str.split(/\r?\n/);
  for (let i = 0; i < linhas.length; i++) {
    if (linhas[i].includes(agulha)) {
      const t = linhas[i].trim();
      saida.push({ linha: i + 1, trecho: t.length > 120 ? t.slice(0, 120) + '...' : t });
    }
  }
  return saida;
}

for (const caminho of arquivos) {
  const bytes = readFileSync(path.join(raiz, caminho));
  let texto;
  try {
    texto = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    // Nao e UTF-8 valido: decodifica sem fatal so para localizar a(s) linha(s).
    const quebrado = new TextDecoder('utf-8').decode(bytes);
    const linhas = linhasCom(quebrado, '\uFFFD');
    for (const l of linhas) {
      problemas.push(`${caminho}:${l.linha}: byte invalido (nao e UTF-8 valido) | ${l.trecho}`);
    }
    if (linhas.length === 0) {
      problemas.push(`${caminho}: nao e UTF-8 valido`);
    }
    continue;
  }
  for (const l of linhasCom(texto, '\uFFFD')) {
    problemas.push(`${caminho}:${l.linha}: U+FFFD no texto | ${l.trecho}`);
  }
}

for (const p of problemas) console.error(p);
console.log(`encoding-check: ${arquivos.length} arquivos verificados, ${problemas.length} problema(s)`);
if (problemas.length > 0) {
  console.error('FALHOU: corrija os arquivos acima e rode de novo.');
  process.exit(1);
}
console.log('OK: todos os arquivos versionados sao UTF-8 valido e sem U+FFFD.');
process.exit(0);
