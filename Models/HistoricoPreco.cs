namespace ControleEstoqueLoja.Models
{
    /// <summary>
    /// Registro imutavel de uma mudanca de PRECO em um produto (item 2 da Fase A).
    ///
    /// E log informativo: nao participa de nenhum calculo de venda, de saldo ou
    /// de fechamento. Existe para o dono responder "isso antes custava quanto?",
    /// ja que o cadastro de produto guarda so o preco ATUAL.
    ///
    /// Os dois precos usam o ConversorCentavos (INTEGER de centavos) porque sao
    /// dinheiro - o mesmo motivo do BUG-20. DataAlteracao fica em TEXT, como
    /// todas as demais datas do sistema.
    /// </summary>
    public class HistoricoPreco
    {
        public int Id { get; set; }
        public int ProdutoId { get; set; }
        public Produto? Produto { get; set; }

        /// <summary>Preco em vigor antes desta alteracao (R$).</summary>
        public decimal PrecoAnterior { get; set; }

        /// <summary>Preco gravado depois desta alteracao (R$).</summary>
        public decimal PrecoNovo { get; set; }

        public DateTime DataAlteracao { get; set; } = DateTime.Now;
    }
}