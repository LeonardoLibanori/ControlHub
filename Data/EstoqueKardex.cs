using ControleEstoqueLoja.Models;

namespace ControleEstoqueLoja.Data;

/// <summary>
/// Regras do Kardex: quantidade sempre positiva, tipo define o sentido,
/// saldos anterior/posterior conferidos no registro. A reversão de uma
/// operação é uma movimentação nova (compensatória), nunca a edição da antiga.
/// </summary>
public static class EstoqueKardex
{
    public const int MaximoCasasDecimais = 3;
    public const decimal QuantidadeMaxima = 1_000_000m;

    public static class Origens
    {
        public const string Venda = "Venda";
        public const string Reposicao = "Reposicao";
        public const string Ajuste = "Ajuste";
        public const string Cadastro = "Cadastro";
        public const string Movimentacao = "Movimentacao";
    }

    public static string ChaveVenda(string chaveIdempotencia, int vendaItemId) =>
        $"venda:{chaveIdempotencia}:{vendaItemId}";

    public static bool TentarValidarQuantidade(decimal quantidade, bool exigeInteiro, out string erro)
    {
        if (quantidade <= 0m)
        {
            erro = "Informe uma quantidade maior que zero.";
            return false;
        }

        if (quantidade > QuantidadeMaxima)
        {
            erro = "A quantidade máxima por movimentação é 1.000.000.";
            return false;
        }

        if (decimal.Round(quantidade, MaximoCasasDecimais) != quantidade)
        {
            erro = "Use no máximo 3 casas decimais.";
            return false;
        }

        if (exigeInteiro && quantidade != decimal.Truncate(quantidade))
        {
            erro = "Este produto é vendido por unidade: informe um número inteiro.";
            return false;
        }

        erro = string.Empty;
        return true;
    }

    public static MovimentacaoEstoque Criar(
        int produtoId,
        TipoMovimentacaoEstoque tipo,
        decimal quantidadeMovimentada,
        decimal saldoAnterior,
        decimal saldoPosterior,
        string? usuarioId,
        string origemTipo,
        int? origemId,
        string? motivo,
        string? chaveCorrelacao)
    {
        if (quantidadeMovimentada <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantidadeMovimentada), "A quantidade movimentada deve ser positiva.");

        if (saldoPosterior < 0m)
            throw new InvalidOperationException("A movimentação deixaria o estoque negativo.");

        if (tipo == TipoMovimentacaoEstoque.Entrada && saldoPosterior != saldoAnterior + quantidadeMovimentada)
            throw new InvalidOperationException("Saldo posterior incompatível com entrada.");

        if (tipo == TipoMovimentacaoEstoque.Saida && saldoPosterior != saldoAnterior - quantidadeMovimentada)
            throw new InvalidOperationException("Saldo posterior incompatível com saída.");

        if (tipo == TipoMovimentacaoEstoque.Ajuste &&
            Math.Abs(saldoPosterior - saldoAnterior) != quantidadeMovimentada)
            throw new InvalidOperationException("Saldo posterior incompatível com ajuste.");

        return new MovimentacaoEstoque
        {
            ProdutoId = produtoId,
            Tipo = tipo,
            QuantidadeMovimentada = quantidadeMovimentada,
            SaldoAnterior = saldoAnterior,
            SaldoPosterior = saldoPosterior,
            DataHora = DateTime.Now,
            UsuarioId = usuarioId,
            Motivo = Truncar(motivo, 200),
            OrigemTipo = origemTipo,
            OrigemId = origemId,
            ChaveCorrelacao = Truncar(chaveCorrelacao, 80)
        };
    }

    private static string? Truncar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return valor;
        var texto = valor.Trim();
        return texto.Length <= maximo ? texto : texto[..maximo];
    }
}
