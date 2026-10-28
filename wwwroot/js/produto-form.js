/* ============================================================================
   produto-form.js - comportamento do formulario de produto (COMMIT 2 + 4)

   Atende DOIS formularios, de forma idempotente por raiz (iniciar(raiz) roda na
   tela cheia e a cada injecao no modal sem duplicar ouvintes):

   * Editar produto (Editar.cshtml): rotulo do preco conforme "Vendido por"
     (atualizarRotuloPreco) + preco em moeda BR (formatarMoeda). Sem #quantidadeVisual.

   * Novo produto (Novo.cshtml): alem do preco, sincroniza o OCULTO #quantidadeReal
     (carrega name="quantidade"; o visivel nao tem name), ajusta unidade (kg/un) e
     rotulo conforme o tipo, sincroniza #estoqueMinimoReal e trata o Enter do leitor
     de codigo de barras (#inputCodigo) para nao submeter antes da hora. Tem #quantidadeVisual.

   COMMIT 4 (modal): a logica que era <script> inline no Novo.cshtml veio para ca.
   Scripts inline NAO executam quando o modal-form.js injeta via importNode, e os
   onchange/oninput globais nao existem no escopo do conteudo importado - por isso
   os atributos inline sairam e tudo e amarrado por addEventListener aqui. O
   modal-form.js chama ProdutoForm.iniciar(corpo) ao injetar; o auto-init abaixo
   cobre a tela cheia (DOMContentLoaded).
   ============================================================================ */
(function () {
    'use strict';

    function iniciar(raiz) {
        if (!raiz) raiz = document;
        var q = function (sel) { return raiz.querySelector ? raiz.querySelector(sel) : null; };

        var tipo = q('#tipoProduto');
        var labelPreco = q('#labelPreco');
        var visual = q('#precoVisual');
        var real = q('#precoReal');

        // exclusivos do NOVO produto
        var qtdVisual = q('#quantidadeVisual');
        var qtdReal = q('#quantidadeReal');
        var labelQtd = q('#labelQuantidade');
        var unidadeQtd = q('#unidadeQuantidade');
        var minVisual = q('#estoqueMinimoVisual');
        var minReal = q('#estoqueMinimoReal');
        var labelMin = q('#labelEstoqueMinimo');
        var unidadeMin = q('#unidadeEstoqueMinimo');
        var custoVisual = q('#custoVisual');
        var custoReal = q('#custoReal');


        // O Novo produto tem #quantidadeVisual; o Editar nao.
        var eNovo = !!qtdVisual;
        if (!tipo && !visual) return;

        function ePeso() { return tipo && tipo.value === 'PorPeso'; }

        function atualizarRotuloPreco() {
            if (!tipo || !labelPreco) return;
            labelPreco.innerText = ePeso() ? 'Preço por kg (R$/kg):' : 'Preço unitário (R$):';
        }

        // Estoque minimo usa a MESMA unidade da quantidade (kg / un).
        function sincronizarEstoqueMinimo() {
            if (!eNovo) return;
            var peso = ePeso();
            if (unidadeMin) unidadeMin.innerText = peso ? 'kg' : 'un';
            if (labelMin) labelMin.innerText = peso ? 'Estoque mínimo (alerta, kg):' : 'Estoque mínimo (alerta, un):';
            if (minVisual) minVisual.step = peso ? 'any' : '1';
            if (minReal) minReal.value = minVisual ? minVisual.value.replace(',', '.') : '';
        }

        function atualizarQuantidade() {
            if (!eNovo) return;
            var peso = ePeso();
            if (labelQtd) labelQtd.innerText = 'Quantidade inicial:';
            if (unidadeQtd) unidadeQtd.innerText = peso ? 'kg' : 'un';
            if (qtdVisual) qtdVisual.step = peso ? 'any' : '1';
            if (qtdReal) qtdReal.value = qtdVisual ? qtdVisual.value.replace(',', '.') : '';
            atualizarRotuloPreco();
            sincronizarEstoqueMinimo();
        }

        function formatarMoeda(input) {
            if (!real) return;
            var valor = input.value.replace(/\D/g, '');
            if (valor === '') { input.value = ''; real.value = ''; return; }
            input.value = (parseInt(valor) / 100).toFixed(2).replace('.', ',');
            real.value = valor;
        }

        // idempotente por elemento: nao reamarra o mesmo node, mas reamarra nodes
        // novos depois de um re-render do servidor.
        if (tipo && !tipo.__pfLigado) {
            tipo.__pfLigado = true;
            tipo.addEventListener('change', function () {
                atualizarRotuloPreco();
                if (eNovo) atualizarQuantidade();
            });
        }
        if (visual && !visual.__pfLigado) {
            visual.__pfLigado = true;
            visual.addEventListener('input', function () { formatarMoeda(visual); });
        }
        if (custoVisual && !custoVisual.__pfLigado) {
            custoVisual.__pfLigado = true;
            custoVisual.addEventListener('input', function () {
                if (!custoReal) return;
                var valor = custoVisual.value.replace(/\D/g, '');
                if (valor === '') { custoVisual.value = ''; custoReal.value = ''; return; }
                custoVisual.value = (parseInt(valor) / 100).toFixed(2).replace('.', ',');
                custoReal.value = valor;
            });
        }
        if (eNovo) {
            if (qtdVisual && !qtdVisual.__pfLigado) {
                qtdVisual.__pfLigado = true;
                qtdVisual.addEventListener('input', atualizarQuantidade);
            }
            if (minVisual && !minVisual.__pfLigado) {
                minVisual.__pfLigado = true;
                minVisual.addEventListener('input', sincronizarEstoqueMinimo);
            }
            // Leitor de codigo de barras "digita" o codigo e completa com Enter:
            // sem isso o Enter submeteria o formulario. Ao bipar, foco vai ao Nome.
            if (inputCodigo && !inputCodigo.__pfLigado) {
                inputCodigo.__pfLigado = true;
                inputCodigo.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        var nome = q('input[name="nome"]');
                        if (nome && nome.focus) nome.focus();
                    }
                });
            }
            atualizarQuantidade();
        } else {
            atualizarRotuloPreco();
        }
    }

    window.ProdutoForm = { iniciar: iniciar };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { iniciar(document); });
    } else {
        iniciar(document);
    }
})();

