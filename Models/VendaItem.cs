namespace ControleEstoqueLoja.Models
{
    public class VendaItem
    {
        public int Id { get; set; }

        public int ProdutoId { get; set; }
        public Produto Produto { get; set; } = null!;

        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Subtotal => Quantidade * PrecoUnitario;

        public int VendaId { get; set; }
        public Venda Venda { get; set; } = null!;
    }
}
