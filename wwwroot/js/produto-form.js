/* ============================================================================
   produto-form.js - comportamento do formulario de produto (COMMIT 2)

   Movido SEM mudar a logica do <script> inline que ficava em
   Views/Home/Editar.cshtml: troca o rotulo do preco conforme "Vendido por"
   (atualizarRotuloPreco) e formata o campo de preco em moeda BR
   (formatarMoeda). Idempotente por raiz: iniciar(raiz) pode ser chamado
   varias vezes (tela cheia e cada injecao no modal) sem duplicar ouvintes.

   Auto-inicia no DOMContentLoaded para a tela cheia; o modal-form.js chama
   ProdutoForm.iniciar(corpo) depois de injetar o conteudo.
   ============================================================================ */
(function () {
    'use strict';

    function iniciar(raiz) {
        if (!raiz) raiz = document;
        var tipo = raiz.querySelector ? raiz.querySelector('#tipoProduto') : null;
        var label = raiz.querySelector ? raiz.querySelector('#labelPreco') : null;
        var visual = raiz.querySelector ? raiz.querySelector('#precoVisual') : null;
        var real = raiz.querySelector ? raiz.querySelector('#precoReal') : null;
        if (!tipo && !visual) return;

        function atualizarRotuloPreco() {
            if (!tipo || !label) return;
            label.innerText = tipo.value === 'PorPeso'
                ? 'Preço por kg (R$/kg):'
                : 'Preço unitário (R$):';
        }

        function formatarMoeda(input) {
            if (!real) return;
            var valor = input.value.replace(/\D/g, '');
            if (valor === '') {
                input.value = '';
                real.value = '';
                return;
            }
            input.value = (parseInt(valor) / 100).toFixed(2).replace('.', ',');
            real.value = valor;
        }

        // idempotente por elemento: nao reamarra o mesmo node (evita disparo
        // duplo), mas reamarra nodes novos depois de um re-render do servidor.
        if (tipo && !tipo.__pfLigado) { tipo.__pfLigado = true; tipo.addEventListener('change', atualizarRotuloPreco); }
        if (visual && !visual.__pfLigado) { visual.__pfLigado = true; visual.addEventListener('input', function () { formatarMoeda(visual); }); }
        atualizarRotuloPreco();
    }

    window.ProdutoForm = { iniciar: iniciar };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { iniciar(document); });
    } else {
        iniciar(document);
    }
})();
