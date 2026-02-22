namespace ControleEstoqueLoja.Models
{
    public class Vendedor
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty; // Esse é o "Cupom"
        public bool Ativo { get; set; } = true;
    }
}
