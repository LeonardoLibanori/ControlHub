/* ============================================================================
   modal-form.js - abre formularios do servidor dentro de um modal (COMMIT 2)

   IIFE, sem dependencias. Reaproveita as MESMAS rotas, o MESMO formulario e o
   MESMO POST das telas cheias: o modal so busca o HTML do GET, injeta o
   [data-modal-conteudo] e, no envio, faz o POST com new FormData(form) +
   token antiforgery (mesma tecnica de repor-estoque.js).

   Gatilho: <a href="/Home/Editar/5" data-modal-form data-modal-titulo="...">.
   Continua link normal com Ctrl/Shift/Meta ou botao do meio.
   ============================================================================ */
(function () {
    'use strict';

    var Z_OVERLAY = 1090;
    var overlayAtual = null;
    var gatilhoAtual = null;

    function tokenDoForm(form) {
        var t = form.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    function fechar(devolverFoco) {
        if (!overlayAtual) return;
        overlayAtual.remove();
        overlayAtual = null;
        document.documentElement.classList.remove('app-modal-aberto');
        document.removeEventListener('keydown', aoTeclado, true);
        if (devolverFoco !== false && gatilhoAtual && gatilhoAtual.focus) gatilhoAtual.focus();
        gatilhoAtual = null;
    }

    function extrairConteudo(html) {
        // NUNCA innerHTML com texto do servidor: parseia e importa o PRIMEIRO
        // [data-modal-conteudo]. Scripts do HTML buscado NAO rodam.
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var alvo = doc.querySelector('[data-modal-conteudo]');
        if (!alvo) return null;
        return document.importNode(alvo, true);
    }

    function mensagemNoCorpo(corpo, texto, linkTelaCheia, href) {
        corpo.textContent = '';
        var p = document.createElement('p');
        p.className = 'app-modal__erro';
        p.setAttribute('role', 'alert');
        p.textContent = texto;
        corpo.appendChild(p);
        if (linkTelaCheia && href) {
            var a = document.createElement('a');
            a.href = href;
            a.textContent = 'Abrir em tela cheia';
            a.className = 'btn btn-outline-secondary btn-sm mt-2 d-inline-block';
            corpo.appendChild(a);
        }
    }

    function focaveis(raiz) {
        var sel = 'a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), ' +
            'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
        return Array.prototype.filter.call(raiz.querySelectorAll(sel), function (el) {
            return el.offsetParent !== null || el === document.activeElement;
        });
    }

    function prenderFoco(e) {
        if (e.key !== 'Tab') return;
        var lista = focaveis(overlayAtual);
        if (!lista.length) return;
        var primeiro = lista[0];
        var ultimo = lista[lista.length - 1];
        if (e.shiftKey && document.activeElement === primeiro) {
            e.preventDefault(); ultimo.focus();
        } else if (!e.shiftKey && document.activeElement === ultimo) {
            e.preventDefault(); primeiro.focus();
        }
    }

    function aoTeclado(e) {
        if (!overlayAtual) return;
        if (e.key === 'Escape') {
            e.stopPropagation();
            fechar(true);
        } else {
            prenderFoco(e);
        }
    }

    function construir(titulo, href) {
        var overlay = document.createElement('div');
        overlay.className = 'app-modal-overlay';
        overlay.style.zIndex = String(Z_OVERLAY);
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');

        var caixa = document.createElement('div');
        caixa.className = 'app-modal';

        var header = document.createElement('div');
        header.className = 'app-modal__header';
        var h = document.createElement('h2');
        h.className = 'app-modal__titulo';
        h.id = 'app-modal-titulo';
        h.textContent = titulo || 'Formulario';
        overlay.setAttribute('aria-labelledby', h.id);
        var btnFechar = document.createElement('button');
        btnFechar.type = 'button';
        btnFechar.className = 'app-modal__fechar';
        btnFechar.setAttribute('aria-label', 'Fechar');
        btnFechar.textContent = '\u00d7';
        btnFechar.addEventListener('click', function () { fechar(true); });
        header.appendChild(h);
        header.appendChild(btnFechar);

        var corpo = document.createElement('div');
        corpo.className = 'app-modal__corpo';
        var carregando = document.createElement('p');
        carregando.className = 'app-modal__carregando';
        carregando.textContent = 'Carregando...';
        corpo.appendChild(carregando);

        caixa.appendChild(header);
        caixa.appendChild(corpo);
        overlay.appendChild(caixa);

        var sujo = false;
        corpo.addEventListener('input', function () { sujo = true; }, true);
        overlay.addEventListener('mousedown', function (e) {
            if (e.target === overlay && !sujo) fechar(true);
        });

        overlay._corpo = corpo;
        return overlay;
    }

    function reativar(botoes) { botoes.forEach(function (b) { b.disabled = false; }); }

    function ligarFormulario(corpo, href) {
        var form = corpo.querySelector('form');
        if (!form) return;
        Array.prototype.forEach.call(corpo.querySelectorAll('[data-modal-fechar]'), function (el) {
            el.addEventListener('click', function (e) { e.preventDefault(); fechar(true); });
        });
        var botoes = Array.prototype.filter.call(form.querySelectorAll('button[type=submit], button:not([type])'), function (b) { return b.type !== 'button'; });
        if (botoes.length) {
            var acoes = document.createElement('div');
            acoes.className = 'app-modal-acoes';
            botoes.forEach(function (b) { form.removeChild(b); acoes.appendChild(b); });
            form.appendChild(acoes);
        }
        if (window.ProdutoForm && window.ProdutoForm.iniciar) {
            try { window.ProdutoForm.iniciar(corpo); } catch (e) { /* ignora */ }
        }
        form.addEventListener('submit', function (e) { e.preventDefault(); enviar(form, href, corpo); });
    }


    function enviar(form, href, corpo) {
        var botoes = Array.prototype.slice.call(form.querySelectorAll('button[type=submit], button:not([type])'))
            .filter(function (b) { return b.type !== 'button'; });
        var desabilitados = [];
        botoes.forEach(function (b) { if (!b.disabled) { b.disabled = true; desabilitados.push(b); } });

        var formData = new FormData(form);
        var token = formData.get('__RequestVerificationToken') || tokenDoForm(form);

        fetch(form.getAttribute('action') || href, {
            method: 'POST', body: formData, credentials: 'same-origin', redirect: 'manual',
            headers: { 'X-CSRF-TOKEN': token || '' }
        }).then(function (resp) {
            if (resp.type === 'opaqueredirect' || resp.status === 0 || (resp.status >= 300 && resp.status < 400)) {
                fechar(false); location.reload(); return null;
            }
            if (resp.status === 400) {
                mensagemNoCorpo(corpo, 'Sua sessao expirou. Recarregue a pagina e tente de novo.', false);
                reativar(desabilitados); return null;
            }
            if (resp.status === 200) {
                return resp.text().then(function (html) {
                    var novo = extrairConteudo(html);
                    if (novo) {
                        corpo.textContent = '';
                        corpo.appendChild(novo);
                        ligarFormulario(corpo, href);
                        var erro = corpo.querySelector('.text-danger, .input-validation-error, [aria-invalid=true]');
                        var alvo = erro || corpo.querySelector('input, select, textarea');
                        if (alvo && alvo.focus) alvo.focus();
                    } else {
                        mensagemNoCorpo(corpo, 'Nao foi possivel salvar. Tente de novo.', true, href);
                    }
                    reativar(desabilitados);
                });
            }
            if (resp.status === 401 || resp.status === 403) {
                mensagemNoCorpo(corpo, 'Voce nao tem permissao para esta acao.', false);
            } else {
                mensagemNoCorpo(corpo, 'Nao foi possivel conectar. Tente novamente.', true, href);
            }
            reativar(desabilitados);
            return null;
        }).catch(function () {
            mensagemNoCorpo(corpo, 'Nao foi possivel conectar. Tente novamente.', true, href);
            reativar(desabilitados);
        });
    }

    function abrir(gatilho) {
        if (overlayAtual) return;
        var href = gatilho.getAttribute('href');
        var titulo = gatilho.getAttribute('data-modal-titulo') || 'Formulario';
        gatilhoAtual = gatilho;

        var overlay = construir(titulo, href);
        overlayAtual = overlay;
        document.body.appendChild(overlay);
        document.documentElement.classList.add('app-modal-aberto');
        document.addEventListener('keydown', aoTeclado, true);

        var corpo = overlay._corpo;
        fetch(href, { credentials: 'same-origin', cache: 'no-store' })
            .then(function (resp) {
                var redirLogin = resp.redirected && /\/Account\/Login/.test(resp.url);
                if (resp.status === 401 || redirLogin) {
                    mensagemNoCorpo(corpo, 'Sua sessao expirou. Entre novamente.', false);
                    var link = document.createElement('a');
                    link.href = '/Account/Login';
                    link.textContent = 'Ir para o login';
                    link.className = 'btn btn-primary btn-sm mt-2 d-inline-block';
                    corpo.appendChild(link);
                    return null;
                }
                var redirNegado = resp.redirected && /AcessoNegado/.test(resp.url);
                if (resp.status === 403 || redirNegado) {
                    mensagemNoCorpo(corpo, 'Voce nao tem permissao para esta acao.', false);
                    return null;
                }
                if (!resp.ok) {
                    mensagemNoCorpo(corpo, 'Nao foi possivel carregar o formulario.', true, href);
                    return null;
                }
                return resp.text();
            })
            .then(function (html) {
                if (!html) return;
                var conteudo = extrairConteudo(html);
                if (!conteudo) { mensagemNoCorpo(corpo, 'Nao foi possivel carregar o formulario.', true, href); return; }
                corpo.textContent = '';
                corpo.appendChild(conteudo);
                ligarFormulario(corpo, href);
                var alvo = corpo.querySelector('input:not([type=hidden]), select, textarea');
                if (alvo && alvo.focus) alvo.focus();
            })
            .catch(function () {
                mensagemNoCorpo(corpo, 'Nao foi possivel carregar o formulario.', true, href);
            });
    }

    document.addEventListener('click', function (e) {
        var g = e.target.closest ? e.target.closest('[data-modal-form]') : null;
        if (!g) return;
        if (e.ctrlKey || e.shiftKey || e.metaKey || e.button === 1) return;
        if (!g.getAttribute('href')) return;
        e.preventDefault();
        abrir(g);
    }, true);
})();

