using System.Collections.Generic;

namespace ControleEstoqueLoja.Models
{
    /// <summary>
    /// Onde o produto fica guardado na loja.
    ///
    /// ATENCAO (generalizacao de unidades): este enum NAO decide mais preco nem
    /// quantidade. Quem decide isso e Produto.Unidade + Produto.PermiteFracionado.
    /// Antes da mudanca, "Granel" era uma regra financeira escondida (o preco da
    /// tabela valia por 100 g e o caixa multiplicava por 10).
    ///
    /// Os codigos sao explicitos porque o SQLite guarda o numero inteiro gravado:
    /// Prateleira=1, Geladeira=2 e Freezer=3 sao os MESMOS valores de antes, para
    /// que nenhuma linha existente precise ser renumerada. "Granel" (0) deixou de
    /// existir: a migration UnidadeMedidaProduto regrava essas linhas como
    /// Prateleira e move o sentido de "a granel" para Unidade="kg" +
    /// PermiteFracionado=1, convertendo o preco (x10) para continuar cobrando o
    /// mesmo valor por quilo.
    /// </summary>
    public enum TipoProduto
    {
        Prateleira = 1,
        Geladeira = 2,
        Freezer = 3
    }
    public class Produto
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty; // Garanta que não seja nulo
        public decimal Preco { get; set; }
        public decimal Quantidade { get; set; }

        /// <summary>
        /// Unidade em que o produto e contado no estoque e vendido no caixa:
        /// "un", "kg", "l", "cx", "pct", "m"... texto livre por proposito (o
        /// comercio muda, o codigo nao deveria mudar junto).
        /// Preco e SEMPRE o preco de UMA unidade dessa medida.
        /// </summary>
        public string Unidade { get; set; } = "un";

        /// <summary>
        /// Permite estoque e venda com quantidade fracionada (ex.: 0,335 kg).
        /// Falso = o caixa recusa quantidade com casas decimais.
        /// </summary>
        public bool PermiteFracionado { get; set; }

        /// <summary>
        /// Local de guarda do produto. Nao interfere em preco nem em quantidade:
        /// ver observacao do enum. O padrao e explicito porque default(TipoProduto)
        /// seria 0, valor que nao existe mais no enum depois que Granel saiu.
        /// </summary>
        public TipoProduto Tipo { get; set; } = TipoProduto.Prateleira;
        public string? CodigoBarras { get; set; }

        /// <summary>
        /// Exclusão lógica (soft delete). Um produto com histórico de vendas NÃO pode ser
        /// apagado fisicamente: a FK VendaItens.ProdutoId tem ON DELETE RESTRICT e o
        /// banco recusaria a exclusão. Desativar o produto o esconde do operação
        /// (as listagens filtram por Ativo) mas preserva todo o histórico financeiro.
        /// </summary>
        public bool Ativo { get; set; } = true;

        /// <summary>Itens de venda que referenciam este produto (histórico financeiro).</summary>
        public List<VendaItem> VendaItems { get; set; } = new List<VendaItem>();
    }
}
