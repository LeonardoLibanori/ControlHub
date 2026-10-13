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
        enviando: false,
        step: 1
    };

    var overlay = document.getElementById('modalRepor');
    var form = document.getElementById('formRepor');
    var titulo = document.getElementById('reporTitulo');
    var input = document.getElementById('reporInput');
    var inputUnit = document.getElementById('reporInputUnit');
    var inputGroup = document.getElementById('reporInputGroup');
    var novoEstoqueEl = document.getElementById('reporNovoEstoque');
    var estoqueAtualEl = document.getElementById('reporEstoqueAtual');
    var hiddenId = document.getElementById('reporHiddenId');
    var hint = document.getElementById('reporHint');
    var alerta = document.getElementById('reporAlerta');
    var btnConfirmar = document.getElementById('reporConfirmar');
    var btnCancelar = document.getElementById('reporCancelar');
    var btnFechar = document.getElementById('reporFechar');
    var btnInc = document.getElementById('reporBtnInc');
    var btnDec = document.getElementById('reporBtnDec');

    // Utilitários
    function $(sel) { return document.querySelector(sel); }
    function $$(sel) { return document.querySelectorAll(sel); }

    function formatarNumeroBR(valor, casas) {
        if (valor === null || valor === undefined || isNaN(valor)) return '—';
        var fixo = Number(valor).toFixed(casas || 3);
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

    function toMilesimos(val) {
        return Math.round(val * 1000);
    }

    function fromMilesimos(mil) {
        return mil / 1000;
    }

    function atualizarPreview() {
        var val = parseQuantidade(input.value);
        var novoEstoque = estado.estoqueAtual;
        if (val !== null && val > 0) {
            novoEstoque += val;
        }
        novoEstoqueEl.textContent = formatarNumeroBR(novoEstoque) + ' ' + estado.unidade;
        if (val !== null && val > 0) {
            novoEstoqueEl.style.color = 'var(--t-green, #16a34a)';
        } else {
            novoEstoqueEl.style.color = 'var(--text-soft, #6b7280)';
        }
    }

    function atualizarHint() {
        if (estado.fracionado) {
            hint.textContent = 'Aceita vírgula ou ponto. Até 3 casas decimais.';
        } else {
            hint.textContent = 'Informe um número inteiro.';
        }
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

    function ajustarQuantidade(delta) {
        var val = parseQuantidade(input.value);
        var base = val !== null && val > 0 ? val : 0;
        var novo = base + delta;
        if (novo < 0) novo = 0;
        // Usar aritmética em milésimos para evitar erro de ponto flutuante
        var milesimos = toMilesimos(novo);
        var arredondado = fromMilesimos(Math.round(milesimos));
        // Formatar com vírgula, no máximo 3 casas, sem zeros à direita
        var fixo = arredondado.toFixed(3);
        var partes = fixo.split('.');
        var decimal = partes[1].replace(/0+$/, '');
        if (decimal.length === 0) {
            input.value = partes[0];
        } else {
            input.value = partes[0] + ',' + decimal;
        }
        atualizarPreview();
        validarInput();
    }

    function abrirModal(btn) {
        estado.btnAberto = btn;
        estado.produtoId = btn.getAttribute('data-id');
        estado.produtoNome = btn.getAttribute('data-nome');
        estado.estoqueAtual = parseFloat(btn.getAttribute('data-estoque') || '0');
        estado.unidade = btn.getAttribute('data-unidade') || 'un';
        estado.fracionado = btn.getAttribute('data-fracionado') === 'true';
        estado.enviando = false;
        estado.step = estado.fracionado ? 0.5 : 1;

        // Preencher modal
        titulo.textContent = 'Repor estoque';
        document.getElementById('reporNomeProduto').textContent = estado.produtoNome;
        estoqueAtualEl.textContent = formatarNumeroBR(estado.estoqueAtual) + ' ' + estado.unidade;
        inputUnit.textContent = estado.unidade;
        hiddenId.value = estado.produtoId;
        input.value = '';
        atualizarPreview();
        atualizarHint();
        alerta.textContent = '';
        alerta.removeAttribute('aria-live');
        alerta.hidden = true;
        btnConfirmar.disabled = true;
        btnConfirmar.textContent = 'Repor';
        btnConfirmar.classList.remove('repor-modal__btn--loading');

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

    // Stepper buttons
    btnInc.addEventListener('click', function (e) {
        e.preventDefault();
        ajustarQuantidade(estado.step);
    });

    btnDec.addEventListener('click', function (e) {
        e.preventDefault();
        ajustarQuantidade(-estado.step);
    });

    // Teclado: setas no input
    input.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowUp') {
            e.preventDefault();
            ajustarQuantidade(estado.step);
        } else if (e.key === 'ArrowDown') {
            e.preventDefault();
            ajustarQuantidade(-estado.step);
        } else if (e.key === 'Enter' && !btnConfirmar.disabled && !estado.enviando) {
            e.preventDefault();
            form.dispatchEvent(new Event('submit'));
        }
    });

    // Preview e validação em tempo real
    input.addEventListener('input', function () {
        atualizarPreview();
        validarInput();
    });

    // Submit do formulário
    form.addEventListener('submit', function (e) {
        e.preventDefault();
        if (btnConfirmar.disabled || estado.enviando) return;
        enviar();
    });

    function enviar() {
        if (estado.enviando) return;
        estado.enviando = true;
        btnConfirmar.disabled = true;
        btnConfirmar.textContent = 'Repondo…';
        btnConfirmar.classList.add('repor-modal__btn--loading');

        var formData = new FormData(form);

        fetch('/Home/Repor', {
            method: 'POST',
            body: formData,
            credentials: 'same-origin',
            headers: {
                'X-CSRF-TOKEN': formData.get('__RequestVerificationToken') || ''
            }
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
                btnConfirmar.classList.remove('repor-modal__btn--loading');
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
            btnConfirmar.classList.remove('repor-modal__btn--loading');
            estado.enviando = false;
            input.focus();
        });
    }
})();