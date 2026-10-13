/* ============================================================================
   REPOR ESTOQUE - JavaScript do modal (IIFE, sem dependências)
   ============================================================================ */
(function () {
    'use strict';

    // Estado
    var estado = {
        btnAberto: null,
        produtoId: null,
        produtoNome: null,
        estoqueAtual: 0,
        unidade: 'un',
        fracionado: false,
        enviando: false
    };

    var overlay = document.getElementById('modalRepor');
    var titulo = document.getElementById('reporTitulo');
    var input = document.getElementById('reporInput');
    var preview = document.getElementById('reporPreview');
    var alerta = document.getElementById('reporAlerta');
    var btnConfirmar = document.getElementById('reporConfirmar');
    var btnCancelar = document.getElementById('reporCancelar');
    var btnFechar = document.getElementById('reporFechar');
    var tokenInput = document.getElementById('reporToken');

    // Utilitários
    function $(sel) { return document.querySelector(sel); }
    function $$(sel) { return document.querySelectorAll(sel); }

    function formatarNumeroBR(valor, casas) {
        if (valor === null || valor === undefined || isNaN(valor)) return '—';
        var fixo = Number(valor).toFixed(casas || 3);
        // Remove zeros à direita desnecessários, mas mantém pelo menos 1 casa se for decimal
        var partes = fixo.split('.');
        var inteiro = partes[0].replace(/\B(?=(\d{3})+(?!\d))/g, '.');
        if (partes.length > 1) {
            var decimal = partes[1].replace(/0+$/, '');
            if (decimal.length === 0) return inteiro;
            return inteiro + ',' + decimal;
        }
        return inteiro;
    }

    function parseQuantidade(texto) {
        if (!texto) return null;
        var limpo = texto.trim().replace(',', '.');
        if (!/^-?\d+(\.\d+)?$/.test(limpo)) return null;
        var val = parseFloat(limpo);
        return isNaN(val) ? null : val;
    }

    function atualizarPreview() {
        var val = parseQuantidade(input.value);
        if (val === null || val <= 0) {
            preview.textContent = 'Novo estoque: —';
            return;
        }
        var novo = estado.estoqueAtual + val;
        var unidade = estado.unidade;
        preview.textContent = 'Novo estoque: ' + formatarNumeroBR(novo) + ' ' + unidade;
    }

    function validarInput() {
        var val = parseQuantidade(input.value);
        var vazio = !input.value.trim();
        var invalido = val === null;
        var menorIgualZero = val !== null && val <= 0;

        btnConfirmar.disabled = vazio || invalido || menorIgualZero;

        if (vazio || invalido || menorIgualZero) {
            alerta.textContent = 'Informe uma quantidade maior que zero.';
            alerta.setAttribute('aria-live', 'polite');
            alerta.hidden = false;
        } else {
            alerta.textContent = '';
            alerta.removeAttribute('aria-live');
            alerta.hidden = true;
        }
    }

    function abrirModal(btn) {
        estado.btnAberto = btn;
        estado.produtoId = btn.getAttribute('data-id');
        estado.produtoNome = btn.getAttribute('data-nome');
        estado.estoqueAtual = parseFloat(btn.getAttribute('data-estoque') || '0');
        estado.unidade = btn.getAttribute('data-unidade') || 'un';
        estado.fracionado = btn.getAttribute('data-fracionado') === 'true';
        estado.enviando = false;

        // Preencher modal
        titulo.textContent = 'Repor estoque: ' + estado.produtoNome;
        document.getElementById('reporEstoqueAtual').textContent =
            'Estoque atual: ' + formatarNumeroBR(estado.estoqueAtual) + ' ' + estado.unidade;
        input.value = '';
        preview.textContent = 'Novo estoque: —';
        alerta.textContent = '';
        alerta.removeAttribute('aria-live');
        alerta.hidden = true;
        btnConfirmar.disabled = true;
        btnConfirmar.textContent = 'Repor';

        // Focar input
        overlay.hidden = false;
        document.body.classList.add('repor-aberto');
        document.documentElement.classList.add('repor-aberto');
        setTimeout(function () { input.focus(); }, 0);

        // Trap focus
        armadilhaFoco();
    }

    function fecharModal() {
        overlay.hidden = true;
        document.body.classList.remove('repor-aberto');
        document.documentElement.classList.remove('repor-aberto');
        if (estado.btnAberto) {
            estado.btnAberto.focus();
            estado.btnAberto = null;
        }
    }

    // Focus trap
    var focusaveis = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
    function armadilhaFoco() {
        var elementos = overlay.querySelectorAll(focusaveis);
        var primeiro = elementos[0];
        var ultimo = elementos[elementos.length - 1];

        overlay.addEventListener('keydown', function trap(e) {
            if (e.key !== 'Tab') return;
            if (e.shiftKey) {
                if (document.activeElement === primeiro) {
                    e.preventDefault();
                    ultimo.focus();
                }
            } else {
                if (document.activeElement === ultimo) {
                    e.preventDefault();
                    primeiro.focus();
                }
            }
        }, { once: false });
    }

    // Delegação de clique nos botões Repor
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-repor]');
        if (btn) {
            e.preventDefault();
            e.stopPropagation();
            abrirModal(btn);
        }
    });

    // Fechar com X
    btnFechar.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        fecharModal();
    });

    // Fechar com Cancelar
    btnCancelar.addEventListener('click', function (e) {
        e.preventDefault();
        fecharModal();
    });

    // Fechar com Escape
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !overlay.hidden) {
            e.preventDefault();
            e.stopPropagation();
            fecharModal();
        }
    });

    // Clique no overlay (fundo) - só fecha se input vazio
    overlay.addEventListener('click', function (e) {
        if (e.target === overlay) {
            if (!input.value.trim()) {
                fecharModal();
            }
        }
    });

    // Preview em tempo real
    input.addEventListener('input', function () {
        atualizarPreview();
        validarInput();
    });

    // Enter no input = enviar
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !btnConfirmar.disabled && !estado.enviando) {
            e.preventDefault();
            enviar();
        }
    });

    // Botão Repor (confirmar)
    btnConfirmar.addEventListener('click', function (e) {
        e.preventDefault();
        if (!btnConfirmar.disabled && !estado.enviando) {
            enviar();
        }
    });

    function enviar() {
        if (estado.enviando) return;
        estado.enviando = true;
        btnConfirmar.disabled = true;
        btnConfirmar.textContent = 'Enviando...';

        var formData = new FormData();
        formData.append('id', estado.produtoId);
        formData.append('quantidade', input.value.trim());
        if (tokenInput) formData.append('__RequestVerificationToken', tokenInput.value);

        var headers = new Headers();
        headers.append('X-CSRF-TOKEN', tokenInput ? tokenInput.value : '');

        fetch('/Home/Repor', {
            method: 'POST',
            body: formData,
            headers: headers,
            credentials: 'same-origin'
        })
        .then(function (resp) {
            if (resp.status === 400) {
                throw { status: 400, message: 'Sua sessão expirou. Recarregue a página e tente de novo.' };
            }
            if (!resp.ok) {
                throw { status: resp.status, message: 'Não foi possível conectar. Tente novamente.' };
            }
            return resp.json();
        })
        .then(function (data) {
            if (data.success) {
                fecharModal();
                var msg = estado.produtoNome + ': novo estoque ' + formatarNumeroBR(data.novoEstoque) + ' ' + data.unidade;
                if (window.Swal) {
                    window.Swal.fire({
                        title: 'Estoque atualizado',
                        text: msg,
                        customClass: { popup: 'swal-success' }
                    }).then(function () {
                        location.reload();
                    });
                } else {
                    location.reload();
                }
            } else {
                alerta.textContent = data.mensagem || 'Erro desconhecido.';
                alerta.setAttribute('aria-live', 'polite');
                alerta.hidden = false;
                btnConfirmar.disabled = false;
                btnConfirmar.textContent = 'Repor';
                estado.enviando = false;
                input.focus();
            }
        })
        .catch(function (err) {
            var msg = (err && err.message) ? err.message : 'Não foi possível conectar. Tente novamente.';
            alerta.textContent = msg;
            alerta.setAttribute('aria-live', 'polite');
            alerta.hidden = false;
            btnConfirmar.disabled = false;
            btnConfirmar.textContent = 'Repor';
            estado.enviando = false;
            input.focus();
        });
    }
})();