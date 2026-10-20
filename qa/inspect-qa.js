// Inspetor de falhas: qa/_out-dashboard.txt
// Uso: node qa/inspect-qa.js [caminho]
const fs = require('fs');
const path = process.argv[2] || 'qa/_out-dashboard.txt';
const raw = fs.readFileSync(path, 'utf16le');
const lines = raw.split(/\r?\n/);
const failures = [];
for (const l of lines) {
  const i = l.indexOf('[FALHA]');
  if (i >= 0) failures.push(l.slice(i + '[FALHA]'.length).trim());
}
console.log('TOTAL ' + failures.length);
const catCount = {};
const det = {};
for (const f of failures) {
  const m = f.match(/^(\S+\/\S+\/\S+)\s+(\S+)\s+(.*)$/);
  if (!m) { catCount['(sem-grupo)'] = (catCount['(sem-grupo)'] || 0) + 1; det[f] = 1; continue; }
  const cat = m[1];
  const desc = m[2] + ' ' + m[3];
  catCount[cat] = (catCount[cat] || 0) + 1;
  det[cat + ' :: ' + desc] = (det[cat + ' :: ' + desc] || 0) + 1;
}
for (const k of Object.keys(catCount).sort()) {
  console.log('\n===== ' + k + ': ' + catCount[k]);
  for (const d of Object.keys(det).sort()) {
    if (d.startsWith(k + ' ::')) {
      console.log('   x' + det[d] + ' :: ' + d.split(' :: ')[1]);
    }
  }
}


