using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

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
        /// Estoque minimo configurado pelo dono, na MESMA unidade da Quantidade
        /// (kg para PorPeso, unidades para os demais). NULL ou 0 = sem alerta.
        ///
        /// NAO e dinheiro e NAO usa o ConversorCentavos: e quantidade, e quantidade
        /// por peso tem 3+ casas (0,125 kg), igual a Quantidade.
        ///
        /// A comparacao de "estoque baixo" e feita em C# (veja EstoqueAbaixoDoMinimo)
        /// e nunca em SQL: decimal cai em TEXT no SQLite e a collation BINARY
        /// ordenaria "10" antes de "9", geraria alerta errado e comeria o WHERE.
        /// </summary>
        public decimal? EstoqueMinimo { get; set; }

        /// <summary>
        /// true apenas quando existe minimo configurado (> 0) E o estoque atual
        /// ficou abaixo dele. Produto sem minimo (null ou 0) NUNCA gera alerta.
        /// </summary>
        [NotMapped]
        public bool EstoqueAbaixoDoMinimo =>
            EstoqueMinimo.HasValue && EstoqueMinimo.Value > 0m && Quantidade < EstoqueMinimo.Value;

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
