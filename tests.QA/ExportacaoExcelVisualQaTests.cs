using ClosedXML.Excel;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace tests.QA;

/// <summary>
/// ETAPA B (ControlHub): testes de VISUAL da exportacao Excel - o que a Etapa A
/// (funcional) nao travava. Aqui o contrato e o DESENHO da planilha: cores do
/// tema, fonte do cabecalho, zebra, formatos numericos, impressao e cache de
/// calculo. Um teste funcional passaria com a planilha toda preta; estes nao.
///
/// ESTES TESTES FALHAM ANTES DA IMPLEMENTACAO, e e o resultado esperado: o
/// gerador (Data/ExportacaoExcel.cs) comeca como esqueleto (NotImplementedException).
///
/// Leem o .xlsx pelo ClosedXML, como o Excel leria - nao ha assert de texto.
/// </summary>
public class ExportacaoExcelVisualQaTests
{
    // Layout vertical (1 marca / 2 legenda / 3 vazia / 4 cabecalho / 5+ dados).
    private const int LinhaMarca = 1;
    private const int LinhaCabecalho = 4;
    private const int PrimeiraLinhaDados = 5;

    // Cores do tema (literais de proposito: se a paleta mudar, o teste tem de gritar).
    private static readonly XLColor CorCabecalho = XLColor.FromHtml("#1F4E5F");
    private static readonly XLColor CorZebra = XLColor.FromHtml("#F2F7F8");
    private static readonly XLColor CorTotal = XLColor.FromHtml("#D9E8EC");
    private static readonly XLColor CorBaixoFundo = XLColor.FromHtml("#FDE2E2");
    private static readonly XLColor Branco = XLColor.FromHtml("#FFFFFF");

    // Formatos numericos (os mesmos literais das constantes do gerador).
    private const string FormatoDinheiro = "R$ #,##0.00";
    private const string FormatoInteira = "#,##0";
    private const string FormatoFracionaria = "#,##0.000";
    private const string FormatoDataHora = "dd/MM/yyyy HH:mm";

    private const int QtdColunasEstoque = 8;
    private const int QtdColunasVendas = 9;

    // =====================================================================
    // INFRAESTRUTURA
    // =====================================================================

    /// <summary>Abre o .xlsx devolvido pelo controller mantendo o stream vivo.</summary>
    private sealed class Planilha : IDisposable
    {
        private readonly MemoryStream _stream;

        public Planilha(FileContentResult arquivo)
        {
            _stream = new MemoryStream(arquivo.FileContents);
            Livro = new XLWorkbook(_stream);
        }

        public XLWorkbook Livro { get; }

        public IXLWorksheet Aba(string nome) => Livro.Worksheet(nome);

        public void Dispose()
        {
            Livro.Dispose();
            _stream.Dispose();
        }
    }

    /// <summary>Ultima linha usada da aba (normalmente a linha de totais).</summary>
    private static int UltimaLinha(IXLWorksheet aba) =>
        aba.RangeUsed()?.LastRow().RowNumber() ?? LinhaCabecalho;

    /// <summary>
    /// Ultima linha de DADOS (a de totais fica logo abaixo). Usada para conferir
    /// o intervalo do AutoFilter, que cobre cabecalho + dados (nao os totais).
    /// </summary>
    private static int UltimaLinhaDados(IXLWorksheet aba) => UltimaLinha(aba) - 1;

    /// <summary>Config em memoria - o mesmo contrato de appsettings.json.</summary>
    private static IConfiguration Config(params (string Chave, string Valor)[] pares)
    {
        var dados = pares.ToDictionary(p => p.Chave, p => (string?)p.Valor);
        return new ConfigurationBuilder().AddInMemoryCollection(dados).Build();
    }

    /// <summary>Uma venda simples, para as assercoes visuais da aba Vendas.</summary>
    private static void SeedVendaPadrao(QaContext qa, DateTime? data = null)
        => qa.SeedVenda(total: 50m, data: data);

    private static FileContentResult Estoque(QaContext qa, string? busca = null) =>
        (FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(busca);

    private static async Task<FileContentResult> Vendas(QaContext qa, DateTime? data = null) =>
        (FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(data);

    // =====================================================================
    // (a) CABECALHO (linha 4): fundo petroleo, fonte branca e negrito
    // =====================================================================

    [Fact]
    public void Estoque_CabecalhoLinha4_DeveTerFundoPetroleoFonteBrancaENegrito()
    {
        using var qa = new QaContext();
        qa.SeedProduto();

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");

        for (var col = 1; col <= QtdColunasEstoque; col++)
        {
            var c = aba.Cell(LinhaCabecalho, col);
            Assert.Equal(CorCabecalho, c.Style.Fill.BackgroundColor);
            Assert.Equal(Branco, c.Style.Font.FontColor);
            Assert.True(c.Style.Font.Bold, $"O cabecalho da coluna {col} nao esta em negrito.");
        }
    }

    [Fact]
    public async Task Vendas_CabecalhoLinha4_DeveTerFundoPetroleoFonteBrancaENegrito()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));
        var aba = planilha.Aba("Vendas");

        for (var col = 1; col <= QtdColunasVendas; col++)
        {
            var c = aba.Cell(LinhaCabecalho, col);
            Assert.Equal(CorCabecalho, c.Style.Fill.BackgroundColor);
            Assert.Equal(Branco, c.Style.Font.FontColor);
            Assert.True(c.Style.Font.Bold, $"O cabecalho da coluna {col} nao esta em negrito.");
        }
    }

    // =====================================================================
    // (b) TITULO (linha 1): fonte petroleo e negrito
    // =====================================================================

    [Fact]
    public void Estoque_TituloLinha1_DeveTerFontePetroleoENegrito()
    {
        using var qa = new QaContext();
        qa.SeedProduto();

        using var planilha = new Planilha(Estoque(qa));
        var titulo = planilha.Aba("Estoque").Cell(LinhaMarca, 1);

        Assert.Equal(CorCabecalho, titulo.Style.Font.FontColor);
        Assert.True(titulo.Style.Font.Bold, "O titulo (linha 1) nao esta em negrito.");
    }

    [Fact]
    public async Task Vendas_TituloLinha1_DeveTerFontePetroleoENegrito()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));
        var titulo = planilha.Aba("Vendas").Cell(LinhaMarca, 1);

        Assert.Equal(CorCabecalho, titulo.Style.Font.FontColor);
        Assert.True(titulo.Style.Font.Bold, "O titulo (linha 1) nao esta em negrito.");
    }

    // =====================================================================
    // (c) PAINEL CONGELADO abaixo do cabecalho (SheetView/SplitRow)
    // =====================================================================

    [Fact]
    public void Estoque_DeveCongelarPainelLogoAbaixoDoCabecalho()
    {
        using var qa = new QaContext();
        qa.SeedProduto();

        using var planilha = new Planilha(Estoque(qa));

        // Cabecalho (linha 4) e o que esta acima ficam congelados: a rolagem
        // comeca na primeira linha de dados.
        Assert.Equal(LinhaCabecalho, planilha.Aba("Estoque").SheetView.SplitRow);
    }

    [Fact]
    public async Task Vendas_DeveCongelarPainelLogoAbaixoDoCabecalho()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));

        Assert.Equal(LinhaCabecalho, planilha.Aba("Vendas").SheetView.SplitRow);
    }

    // =====================================================================
    // (d) AUTO-FILTER ativo sobre cabecalho + dados
    // =====================================================================

    [Fact]
    public void Estoque_AutoFilterDeveCobrirCabecalhoEDados()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "ARROZ");
        qa.SeedProduto(nome: "FEIJAO");

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");

        Assert.True(aba.AutoFilter.IsEnabled, "O AutoFilter nao esta ativo.");
        var faixa = aba.AutoFilter.Range;
        Assert.NotNull(faixa);
        Assert.Equal(LinhaCabecalho, faixa!.FirstRow().RowNumber());
        Assert.Equal(UltimaLinhaDados(aba), faixa.LastRow().RowNumber());
    }

    [Fact]
    public async Task Vendas_AutoFilterDeveCobrirCabecalhoEDados()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));
        var aba = planilha.Aba("Vendas");

        Assert.True(aba.AutoFilter.IsEnabled, "O AutoFilter nao esta ativo.");
        var faixa = aba.AutoFilter.Range;
        Assert.NotNull(faixa);
        Assert.Equal(LinhaCabecalho, faixa!.FirstRow().RowNumber());
        Assert.Equal(UltimaLinhaDados(aba), faixa.LastRow().RowNumber());
    }

    // =====================================================================
    // (e) LINHA DE TOTAIS: fundo #D9E8EC e negrito
    // =====================================================================

    [Fact]
    public void Estoque_LinhaDeTotais_DeveTerFundoENegrito()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "ARROZ", preco: 5m, quantidade: 2m);

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");
        var linha = UltimaLinha(aba);

        for (var col = 1; col <= QtdColunasEstoque; col++)
        {
            var c = aba.Cell(linha, col);
            Assert.Equal(CorTotal, c.Style.Fill.BackgroundColor);
            Assert.True(c.Style.Font.Bold, $"A linha de totais (coluna {col}) nao esta em negrito.");
        }
    }

    [Fact]
    public async Task Vendas_LinhaDeTotais_DeveTerFundoENegrito()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));
        var aba = planilha.Aba("Vendas");
        var linha = UltimaLinha(aba);

        for (var col = 1; col <= QtdColunasVendas; col++)
        {
            var c = aba.Cell(linha, col);
            Assert.Equal(CorTotal, c.Style.Fill.BackgroundColor);
            Assert.True(c.Style.Font.Bold, $"A linha de totais (coluna {col}) nao esta em negrito.");
        }
    }

    // =====================================================================
    // (f) ZEBRA: linhas de dados alternadas (#F2F7F8); estoque baixo manda
    // =====================================================================

    [Fact]
    public void Estoque_ZebraDeveAlternarAsLinhasDeDados()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "AAA", preco: 1m, quantidade: 1m);
        qa.SeedProduto(nome: "BBB", preco: 1m, quantidade: 1m);
        qa.SeedProduto(nome: "CCC", preco: 1m, quantidade: 1m);
        qa.SeedProduto(nome: "DDD", preco: 1m, quantidade: 1m);

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");

        // Linhas 5..8. A zebra cai nas alternadas (6 e 8); 5 e 7 ficam limpas.
        Assert.Equal(CorZebra, aba.Cell(PrimeiraLinhaDados + 1, 1).Style.Fill.BackgroundColor);
        Assert.Equal(CorZebra, aba.Cell(PrimeiraLinhaDados + 3, 1).Style.Fill.BackgroundColor);
        Assert.NotEqual(CorZebra, aba.Cell(PrimeiraLinhaDados, 1).Style.Fill.BackgroundColor);
        Assert.NotEqual(CorZebra, aba.Cell(PrimeiraLinhaDados + 2, 1).Style.Fill.BackgroundColor);
    }

    [Fact]
    public void Estoque_EstoqueBaixoDeveTerPrioridadeSobreAZebra()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "AAA", preco: 1m, quantidade: 100m, estoqueMinimo: 10m);
        // BBB cai na linha 6 (posicao de zebra) E esta abaixo do minimo: o
        // vermelho de alerta tem de vencer a zebra.
        qa.SeedProduto(nome: "BBB", preco: 1m, quantidade: 1m, estoqueMinimo: 10m);

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");

        var zebrada = aba.Cell(PrimeiraLinhaDados + 1, 1);
        Assert.NotEqual(CorZebra, zebrada.Style.Fill.BackgroundColor);
        Assert.Equal(CorBaixoFundo, zebrada.Style.Fill.BackgroundColor);

        var limpa = aba.Cell(PrimeiraLinhaDados, 1);
        Assert.NotEqual(CorBaixoFundo, limpa.Style.Fill.BackgroundColor);
    }

    // =====================================================================
    // (g) FORMATO NUMERICO das colunas
    // =====================================================================

    [Fact]
    public void Estoque_FormatosNumericos_DevemSeguirOTipo()
    {
        using var qa = new QaContext();
        // ARROZ: inteiro (prateleira). BANANA: por peso, fracionario.
        qa.SeedProduto(nome: "ARROZ", preco: 5m, quantidade: 40m, estoqueMinimo: 10m);
        qa.SeedProduto(nome: "BANANA", preco: 3m, quantidade: 0.125m,
                       tipo: TipoProduto.PorPeso, estoqueMinimo: 0.5m);

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");

        var arroz = LinhaDe(aba, "ARROZ");
        var banana = LinhaDe(aba, "BANANA");

        // Dinheiro: Preco (D) e Valor em estoque (H).
        Assert.Equal(FormatoDinheiro, aba.Cell(arroz, 4).Style.NumberFormat.Format);
        Assert.Equal(FormatoDinheiro, aba.Cell(arroz, 8).Style.NumberFormat.Format);

        // Quantidade inteira -> #,##0; fracionaria -> #,##0.000.
        Assert.Equal(FormatoInteira, aba.Cell(arroz, 5).Style.NumberFormat.Format);
        Assert.Equal(FormatoInteira, aba.Cell(arroz, 6).Style.NumberFormat.Format);
        Assert.Equal(FormatoFracionaria, aba.Cell(banana, 5).Style.NumberFormat.Format);
        Assert.Equal(FormatoFracionaria, aba.Cell(banana, 6).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task Vendas_FormatosNumericos_DevemSeguirOTipo()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa, new DateTime(2026, 2, 10, 14, 30, 0));

        using var planilha = new Planilha(await Vendas(qa));
        var aba = planilha.Aba("Vendas");

        Assert.Equal(FormatoDataHora, aba.Cell(PrimeiraLinhaDados, 2).Style.NumberFormat.Format);
        Assert.Equal(FormatoInteira, aba.Cell(PrimeiraLinhaDados, 3).Style.NumberFormat.Format);
        Assert.Equal(FormatoDinheiro, aba.Cell(PrimeiraLinhaDados, 7).Style.NumberFormat.Format);
        Assert.Equal(FormatoDinheiro, aba.Cell(PrimeiraLinhaDados, 8).Style.NumberFormat.Format);
        Assert.Equal(FormatoDinheiro, aba.Cell(PrimeiraLinhaDados, 9).Style.NumberFormat.Format);
    }

    /// <summary>Linha do produto pelo nome (coluna A).</summary>
    private static int LinhaDe(IXLWorksheet aba, string nome)
    {
        for (var linha = PrimeiraLinhaDados; linha <= UltimaLinha(aba); linha++)
        {
            if (aba.Cell(linha, 1).GetString() == nome) return linha;
        }

        throw new InvalidOperationException($"Produto '{nome}' nao esta na planilha.");
    }

    // =====================================================================
    // (h) IMPRESSAO: paisagem, 1 pagina de largura, cabecalho repetido
    // =====================================================================

    [Fact]
    public void Estoque_Impressao_DeveSerPaisagemUmaPaginaDeLarguraComCabecalhoRepetido()
    {
        using var qa = new QaContext();
        qa.SeedProduto();

        using var planilha = new Planilha(Estoque(qa));
        var ps = planilha.Aba("Estoque").PageSetup;

        Assert.Equal(XLPageOrientation.Landscape, ps.PageOrientation);
        Assert.Equal(1, ps.PagesWide);
        Assert.Equal(0, ps.PagesTall);
        Assert.Equal(LinhaCabecalho, ps.FirstRowToRepeatAtTop);
        Assert.Equal(LinhaCabecalho, ps.LastRowToRepeatAtTop);
    }

    [Fact]
    public async Task Vendas_Impressao_DeveSerPaisagemUmaPaginaDeLarguraComCabecalhoRepetido()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));
        var ps = planilha.Aba("Vendas").PageSetup;

        Assert.Equal(XLPageOrientation.Landscape, ps.PageOrientation);
        Assert.Equal(1, ps.PagesWide);
        Assert.Equal(0, ps.PagesTall);
        Assert.Equal(LinhaCabecalho, ps.FirstRowToRepeatAtTop);
        Assert.Equal(LinhaCabecalho, ps.LastRowToRepeatAtTop);
    }

    // =====================================================================
    // (i) LARGURAS: entre 8 e 60; o titulo da linha 1 NAO alarga a coluna A
    // =====================================================================

    [Fact]
    public void Estoque_LargurasDevemFicarEntre8E60()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);

        using var planilha = new Planilha(Estoque(qa));
        var aba = planilha.Aba("Estoque");

        for (var col = 1; col <= QtdColunasEstoque; col++)
        {
            var largura = aba.Column(col).Width;
            Assert.True(largura >= 8, $"A coluna {col} ficou com largura {largura} (< 8).");
            Assert.True(largura <= 60, $"A coluna {col} ficou com largura {largura} (> 60).");
        }
    }

    [Fact]
    public void Estoque_TituloLongoNaoDeveAlargarAColunaA()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "ARROZ", preco: 1m, quantidade: 1m);
        // Titulo de 90 caracteres: se ele entrasse no autosize, a largura iria
        // para o teto. Nomes/rotulos sao curtos, entao a coluna A tem de ficar estreita.
        var config = Config(("Marca:Nome", new string('W', 90)));

        using var planilha = new Planilha(
            (FileContentResult)new HomeController(qa.Context, config).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");

        Assert.True(aba.Column(1).Width < 40,
            $"A coluna A foi alargada pelo titulo: {aba.Column(1).Width}.");
    }

    // =====================================================================
    // (j) CACHE DE CALCULO: FullCalculationOnLoad = true
    // =====================================================================

    [Fact]
    public void Estoque_DeveForcarRecalculoNoLoad()
    {
        using var qa = new QaContext();
        qa.SeedProduto();

        using var planilha = new Planilha(Estoque(qa));

        Assert.True(planilha.Livro.FullCalculationOnLoad,
            "O livro nao pede recalc no load: as formulas SUM/COUNT podem abrir zeradas.");
    }

    [Fact]
    public async Task Vendas_DeveForcarRecalculoNoLoad()
    {
        using var qa = new QaContext();
        SeedVendaPadrao(qa);

        using var planilha = new Planilha(await Vendas(qa));

        Assert.True(planilha.Livro.FullCalculationOnLoad,
            "O livro nao pede recalc no load: as formulas SUM/COUNT podem abrir zeradas.");
    }
}
