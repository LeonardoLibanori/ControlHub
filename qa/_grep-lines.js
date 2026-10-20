// Grava no arquivo cada linha do qa/_out-dashboard.txt que contém o trecho
// indicado, em UTF-8 (evita a codificação UTF-16LE do q.a.).
// Uso: node qa/_grep-lines.js <trecho> [saida]
const fs = require('fs');
const trecho = process.argv[2];
const saida = process.argv[3] || 'qa/_grep-out.txt';
const raw = fs.readFileSync('qa/_out-dashboard.txt', 'utf16le');
const lines = raw.split(/\r?\n/);
let out = '';
for (const l of lines) {
  if (l.includes(trecho)) out += l + '\n';
}
fs.writeFileSync(saida, out, 'utf8');
console.log('saved ' + saida + ' (' + out.split('\n').length + ' lines)');