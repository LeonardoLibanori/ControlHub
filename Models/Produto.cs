using System.Collections.Generic;

namespace ControleEstoqueLoja.Models
{
    public enum TipoProduto
    {
        PorPeso = 0,
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
        public TipoProduto Tipo { get; set; }
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
