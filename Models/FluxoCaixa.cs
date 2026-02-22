public class FluxoCaixa
{
    public int Id { get; set; }
    public DateTime Data { get; set; } = DateTime.Now;

    // Tipo: "Sangria", "Suprimento", "Abertura" ou "Fechamento"
    public string Tipo { get; set; } = string.Empty;

    public decimal Valor { get; set; }
    public string? Observacao { get; set; }
}