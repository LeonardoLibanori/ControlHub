using System;

namespace ControleEstoqueLoja.Models
{
    /// <summary>
    /// Tipo da movimentação no Kardex. A quantidade gravada é sempre o
    /// valor absoluto (&gt; 0); o sentido vem do tipo e dos saldos.
    /// </summary>
    public enum TipoMovimentacaoEstoque
    {
        Entrada = 1,
        Saida = 2,
        Ajuste = 3
    }

    /// <summary>
    /// Registro imutável de uma alteração efetiva de quantidade de um produto.
    /// Não substitui o saldo em <see cref="Produto.Quantidade"/>: o Kardex
    /// explica como o saldo chegou ao valor atual.
    /// </summary>
    public class MovimentacaoEstoque
    {
        public int Id { get; set; }
        public int ProdutoId { get; set; }
        public Produto? Produto { get; set; }

        public TipoMovimentacaoEstoque Tipo { get; set; }

        /// <summary>Magnitude positiva da movimentação, na unidade do produto.</summary>
        public decimal QuantidadeMovimentada { get; set; }

        public decimal SaldoAnterior { get; set; }
        public decimal SaldoPosterior { get; set; }

        public DateTime DataHora { get; set; } = DateTime.Now;

        /// <summary>Id do Identity (AspNetUsers). Nulo em operações da suíte sem usuário autenticado.</summary>
        public string? UsuarioId { get; set; }

        public string? Motivo { get; set; }

        /// <summary>Origem comercial: Venda, Reposicao, Ajuste, Cadastro, Movimentacao.</summary>
        public string OrigemTipo { get; set; } = string.Empty;

        public int? OrigemId { get; set; }

        /// <summary>
        /// Chave única para impedir duplicidade (ex.: venda:{guid}:{itemId}).
        /// Nulo quando a operação não tem idempotência própria (reposição avulsa).
        /// </summary>
        public string? ChaveCorrelacao { get; set; }
    }
}
