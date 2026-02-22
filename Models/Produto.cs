namespace ControleEstoqueLoja.Models
{
    public enum TipoProduto
    {
        Granel,
        Prateleira,
        Geladeira,
        Freezer
    }
    public class Produto
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty; // Garanta que não seja nulo
        public decimal Preco { get; set; }
        public decimal Quantidade { get; set; }
        public TipoProduto Tipo { get; set; }
        public string? CodigoBarras { get; set; }
    }
}
