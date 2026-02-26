using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControleEstoqueLoja.Models
{
    public class Venda
    {
        public int Id { get; set; }
        public DateTime DataVenda { get; set; }
        public decimal TotalVenda { get; set; }

        // ESTA LINHA É A QUE ESTÁ FALTANDO:
        public string MetodoPagamento { get; set; }

        public string? CPFCliente { get; set; }

        // Novos campos para imprimir na nota
        public decimal ValorRecebido { get; set; } = 0m;
        public decimal ValorTroco { get; set; } = 0m;

        public List<VendaItem> Itens { get; set; }
        public bool IsFechado { get; set; } = false;

        [NotMapped]
        public List<VendaItem> ItensTemporarios { get; set; }
        public string? CodigoVendedor { get; set; } // O nosso "Cupom"
    }
}
