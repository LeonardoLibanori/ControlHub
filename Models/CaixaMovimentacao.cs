using System;

namespace ControleEstoqueLoja.Models
{
    public class CaixaMovimentacao
    {
        public int Id { get; set; }
        public DateTime Data { get; set; }
        public string Tipo { get; set; } = string.Empty; // Entrada ou Saída
        public decimal Valor { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public bool IsFechado { get; set; } = false;
    }
}
