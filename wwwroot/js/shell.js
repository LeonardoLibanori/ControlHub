/*
 * wwwroot/js/shell.js  —  AJUSTE DE SHELL (passos 1-2 do redesign).
 * Comportamento do shell autenticado. Sem dependencia (nao usa jQuery nem
 * Bootstrap): menu do usuario, chave de tema e recolher/abrir a sidebar.
 *
 * Chaves de localStorage:
 *   controlhub-tema     -> 'auto' | 'claro' | 'escuro' (ja existia)
 *   controlhub-sidebar  -> 'aberta' | 'fechada' (recolher em >= 1024px)
 *
 * O tema claro/escuro inicial (sem piscar) e aplicado pelo script minimo do
 * <head> do _Layout. Aqui so reagimos ao clique.
 */
(function () {
    'use strict';

    var raiz = document.documentElement;
    var CHAVE_TEMA = 'controlhub-tema';
    var CHAVE_SIDEBAR = 'controlhub-sidebar';
    var LARGURA_GAVETA = 1024;

    function $(id) { return document.getElementById(id); }
    function temaEfetivo() {
        var salvo = null;
        try { salvo = localStorage.getItem(CHAVE_TEMA); } catch (e) { }
        if (salvo === 'claro' || salvo === 'escuro') return salvo;
        var escuro = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        return escuro ? 'escuro' : 'claro';
    }
    function temaAtual() { return raiz.getAttribute('data-theme') === 'light' ? 'claro' : 'escuro'; }
    function aplicarTema(valor) {
        if (valor === 'claro') raiz.setAttribute('data-theme', 'light');
        else if (valor === 'escuro') raiz.setAttribute('data-theme', 'dark');
        else raiz.removeAttribute('data-theme');
        try { localStorage.setItem(CHAVE_TEMA, valor); } catch (e) { }
        if (window.CONTROLHUB_TEMA) window.CONTROLHUB_TEMA.valor = valor;
        refletirChave();
    }

    /* ----- CHAVE DE TEMA (passo 2) --------------------------------------- */
    var chave = document.querySelector('.app-theme');
    function refletirChave() {
        if (!chave) return;
        chave.setAttribute('aria-checked', temaAtual() === 'escuro' ? 'true' : 'false');
    }
    if (chave) {
        refletirChave();
        chave.addEventListener('click', function () {
            aplicarTema(temaAtual() === 'escuro' ? 'claro' : 'escuro');
        });
    }

    /* ----- MENU DO USUARIO (passo 1) ------------------------------------- */
    var botaoUsuario = $('btnUsuario');
    var menuUsuario = $('menuUsuario');

    function itensMenu() {
        return menuUsuario ? Array.prototype.slice.call(menuUsuario.querySelectorAll('[role="menuitem"]')) : [];
    }
    function menuAberto() { return !!(menuUsuario && menuUsuario.classList.contains('aberto')); }
    function abrirMenu() {
        if (!menuUsuario || !botaoUsuario) return;
        menuUsuario.classList.add('aberto');
        menuUsuario.removeAttribute('aria-hidden');
        botaoUsuario.setAttribute('aria-expanded', 'true');
        var itens = itensMenu();
        if (itens.length) itens[0].focus();
    }
    function fecharMenu(voltarFoco) {
        if (!menuUsuario || !botaoUsuario) return;
        menuUsuario.classList.remove('aberto');
        menuUsuario.setAttribute('aria-hidden', 'true');
        botaoUsuario.setAttribute('aria-expanded', 'false');
        if (voltarFoco !== false) botaoUsuario.focus();
    }

    if (botaoUsuario && menuUsuario) {
        botaoUsuario.addEventListener('click', function (e) {
            e.stopPropagation();
            if (menuAberto()) fecharMenu(true); else abrirMenu();
        });
        menuUsuario.addEventListener('keydown', function (e) {
            var itens = itensMenu();
            var i = itens.indexOf(document.activeElement);
            if (e.key === 'Escape') { e.preventDefault(); fecharMenu(true); }
            else if (e.key === 'ArrowDown') { e.preventDefault(); if (itens.length) itens[(i + 1 + itens.length) % itens.length].focus(); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); if (itens.length) itens[(i - 1 + itens.length) % itens.length].focus(); }
        });
        botaoUsuario.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && menuAberto()) { e.preventDefault(); fecharMenu(true); }
        });
        document.addEventListener('click', function (e) {
            if (!menuAberto()) return;
            if (menuUsuario.contains(e.target) || botaoUsuario.contains(e.target)) return;
            fecharMenu(false);
        });
    }

    /* ----- RECOLHER / ABRIR SIDEBAR (passo 1) ---------------------------- */
    var btnMenu = $('btnMenu');
    var sidebar = $('app-sidebar');
    var cortina = $('cortinaSidebar');

    function estadoSidebar() {
        try { return localStorage.getItem(CHAVE_SIDEBAR); } catch (e) { return null; }
    }
    function ehDesktop() { return window.innerWidth >= LARGURA_GAVETA; }
    function marcarFechada(fechada) {
        if (fechada) raiz.setAttribute('data-sidebar', 'fechada');
        else raiz.removeAttribute('data-sidebar');
        if (btnMenu) btnMenu.setAttribute('aria-expanded', fechada ? 'false' : 'true');
    }
    function abrirGaveta() {
        if (!sidebar) return;
        sidebar.classList.add('aberto');
        if (cortina) cortina.classList.add('ativo');
        if (btnMenu) btnMenu.setAttribute('aria-expanded', 'true');
    }
    function fecharGaveta() {
        if (!sidebar) return;
        sidebar.classList.remove('aberto');
        if (cortina) cortina.classList.remove('ativo');
        if (btnMenu) btnMenu.setAttribute('aria-expanded', 'false');
        if (btnMenu) btnMenu.focus();
    }
    function alternar() {
        if (ehDesktop()) {
            var atual = estadoSidebar();
            // Inverte o estado atual: primeiro clique (null/aberta) FECHA,
            // proximo ABRE. O clique do usuario e o pedido - nao se ignora.
            var fechada = atual !== 'fechada';
            try { localStorage.setItem(CHAVE_SIDEBAR, fechada ? 'fechada' : 'aberta'); } catch (e) { }
            marcarFechada(fechada);
        } else if (sidebar && sidebar.classList.contains('aberto')) {
            fecharGaveta();
        } else {
            abrirGaveta();
        }
    }

    if (btnMenu && sidebar) {
        btnMenu.addEventListener('click', alternar);
    }
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        if (menuAberto()) return;
        if (sidebar && sidebar.classList.contains('aberto')) fecharGaveta();
    });
    if (cortina) cortina.addEventListener('click', fecharGaveta);
    if (sidebar) {
        sidebar.addEventListener('click', function (e) {
            var a = e.target.closest && e.target.closest('a');
            if (a && !ehDesktop() && menuAberto()) fecharMenu(false);
            if (a && !ehDesktop()) fecharGaveta();
        });
    }
    var ultimoDesktop = ehDesktop();
    window.addEventListener('resize', function () {
        var agora = ehDesktop();
        if (agora !== ultimoDesktop) {
            ultimoDesktop = agora;
            if (sidebar) sidebar.classList.remove('aberto');
            if (cortina) cortina.classList.remove('ativo');
        }
    });
    if (ehDesktop()) marcarFechada(estadoSidebar() === 'fechada');

    // exposto para os testes de QA
    window.SHELL = {
        tema: temaAtual, aplicarTema: aplicarTema, temaEfetivo: temaEfetivo,
        menuAberto: menuAberto, abrirMenu: abrirMenu, fecharMenu: fecharMenu,
        abrirGaveta: abrirGaveta, fecharGaveta: fecharGaveta, alternar: alternar
    };
})();

/* SweetAlert2 nao pode mexer no layout do shell: sem height:auto no html/body
   e sem padding-right de "scrollbar". Vale para todos os Swal.fire do sistema. */
(function () {
    function ajustar() {
        if (!window.Swal || window.Swal.__controlhub) { return; }
        var ajustado = window.Swal.mixin({ heightAuto: false, scrollbarPadding: false });
        ajustado.__controlhub = true;
        window.Swal = ajustado;
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', ajustar);
    } else {
        ajustar();
    }
})();