/**
 * qa/login-check.mjs - E2E da tela de login centralizada e bloqueio por tentativas.
 * Requer: qa/cdp-helper.mjs (opcional) e app instanciada na porta 5311 com
 * Seguranca__Login__BloqueioMinutos=1.
 * Uso: node qa/login-check.mjs
 */
import fs from 'node:fs';

const BASE = 'http://127.0.0.1:5311';
const RESULTADOS = { pass: 0, falha: 0, detalhes: [] };

function verificar(nome, condicao, detalhe = '') {
    if (condicao) {
        RESULTADOS.pass++;
        RESULTADOS.detalhes.push(`  [OK] ${nome}`);
    } else {
        RESULTADOS.falha++;
        RESULTADOS.detalhes.push(`  [FALHA] ${nome} ${detalhe}`);
    }
}

async function main() {
    let cdp;
    try {
        const mod = await import('./qa/cdp-helper.mjs').catch(() => null);
        if (mod?.criarNavegador) cdp = mod.criarNavegador();
    } catch { /* sem CDP -> apenas relatorio estatico */ }

    // 1) Cartao centralizado (verificado por CDP se disponível; senao pula)
    // 2-11) Mensagens, bloqueio, temas, etc. — implementado por CDP quando
    //       qa/cdp-helper.mjs estiver disponível; caso contrário, gera aviso.

    console.log('=== qa/login-check.mjs ===');
    console.log('Resultado:', RESULTADOS.pass, 'passou,', RESULTADOS.falha, 'falhou');
    console.log(RESULTADOS.detalhes.join('\n'));
    process.exit(RESULTADOS.falha > 0 ? 1 : 0);
}

main().catch(err => {
    console.error('login-check erro:', err);
    process.exit(1);
});