using System;

namespace ControleEstoqueLoja.Models
{
    /// <summary>
    /// Trilha comercial: quem fez o quê, em qual entidade, com valores
    /// anteriores/posteriores quando couber. Não guarda senha, cookie, CPF
    /// nem segredo. O Kardex cobre quantidade; esta tabela cobre a ação.
    /// </summary>
    public class RegistroAuditoria
    {
        public int Id { get; set; }
        public DateTime DataHora { get; set; } = DateTime.Now;
        public string? UsuarioId { get; set; }
        public string Acao { get; set; } = string.Empty;
        public string Entidade { get; set; } = string.Empty;
        public string? EntidadeId { get; set; }
        /// <summary>
        /// Nome descritivo da entidade no momento do evento (ex: "Arroz 5 kg").
        /// Preenchido na gravação para preservar o histórico mesmo que o cadastro
        /// seja alterado ou o produto seja excluído posteriormente.
        /// </summary>
        public string? EntidadeNome { get; set; }
        public string? ValorAnterior { get; set; }
        public string? ValorNovo { get; set; }
        public string? Motivo { get; set; }
    }
}
