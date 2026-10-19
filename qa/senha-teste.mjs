/*
 * qa/senha-teste.mjs - senhas de teste GERADAS por execucao (Fase 0/C).
 *
 * NUNCA grava senha fixa aqui: usa SHELL_DONO_SENHA / SHELL_OPERADOR_SENHA
 * (nomes da Fase 0/C) e os legados SHELL_OPER_SENHA quando existirem; senao
 * GERA uma senha aleatoria que cumpre a politica do Identity em Program.cs
 * (RequiredLength 6, maiuscula, minuscula, digito - mais simbolo por folga)
 * UMA VEZ por execucao e a publica no process.env para os scripts filhos.
 *
 * Uso:
 *   import { senhaDono, senhaOperador } from './senha-teste.mjs';
 */
import { randomInt } from 'node:crypto';

const MAIUS = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
const MINUS = 'abcdefghijkmnpqrstuvwxyz';
const DIG = '23456789';
const SIMB = '!@#$%&*-+=?';
const TODAS = MAIUS + MINUS + DIG + SIMB;

function gerar() {
    const p = (s) => s[randomInt(s.length)];
    const resto = Array.from({ length: 10 }, () => p(TODAS)).join('');
    return p(MAIUS) + p(MINUS) + p(DIG) + p(SIMB) + resto;
}

function resolver(...nomes) {
    for (const n of nomes) {
        const v = process.env[n];
        if (v) return v;
    }
    const s = gerar();
    for (const n of nomes) process.env[n] = s;
    return s;
}

export function senhaDono() {
    return resolver('SHELL_DONO_SENHA');
}

export function senhaOperador() {
    return resolver('SHELL_OPERADOR_SENHA', 'SHELL_OPER_SENHA');
}
