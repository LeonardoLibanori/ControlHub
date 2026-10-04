using ClosedXML.Excel;
using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text;

namespace tests.QA;

/// <summary>
/// ETAPA A (ControlHub): exportacao em EXCEL (.xlsx) - o arquivo NOVO, ao lado do
/// CSV. O CSV e seus 28 testes (ExportacaoCsvQaTests.cs) ficam intactos.
///
/// POR QUE ESTE ARQUIVO ABRE O ARQUIVO DE VERDADE: o valor do Excel nao esta no
/// texto, esta no TIPO da celula. Um teste que so procurasse "5,99" no arquivo
/// passaria com o preco gravado como STRING - e a planilha nao somaria, que e
/// exatamente o defeito que ela veio corrigir. Por isso tudo aqui passa pelo
/// ClosedXML, lendo o .xlsx como o Excel leria.
///
/// O teste mais importante continua sendo o de INJECAO: um produto chamado
/// "=1+1" nao pode virar formula na planilha de quem abrir o arquivo.
///
/// ESTES TESTES FALHAM NESTA ETAPA, e e o resultado esperado: o gerador
/// (Data/ExportacaoExcel.cs) e um esqueleto que lanca NotImplementedException.
/// Eles passam quando o gerador for implementado.
/// </summary>
public class ExportacaoExcelQaTests
{
    /// <summary>Content-Type do .xlsx - o que faz o navegador BAIXAR o arquivo.</summary>
    private const string ContentTypeXlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Layout (1 marca / 2 legenda / 3 vazia / 4 cabecalho / 5+ dados).
    // Literais de proposito: se o layout mudar, o teste tem de gritar.
    private const int LinhaMarca = 1;
    private const int LinhaLegenda = 2;
    private const int LinhaCabecalho = 4;
    private const int PrimeiraLinhaDados = 5;

    private static readonly string[] CabecalhoEstoque =
    {
        "Produto", "Unidade", "Preço (R$)", "Estoque",
        "Estoque mínimo", "Estoque baixo", "Valor em estoque (R$)"
    };

    private static readonly string[] CabecalhoVendas =
    {
        "Venda", "Data", "Itens", "Quantidade total", "Método de pagamento",
        "Vendedor (cupom)", "Valor recebido", "Troco", "Total (R$)"
    };

    // =====================================================================
    // INFRAESTRUTURA
    // =====================================================================

    /// <summary>
    /// Abre o .xlsx entregue pelo controller e MANTEM o MemoryStream vivo
    /// enquanto o livro e usado: o ClosedXML le o stream de forma preguicosa, e
    /// um using que fecha o stream cedo derrubaria o teste com erro de leitura
    /// em vez de falhar na assercao. De quebra, valida de uma vez so os
    /// cabecalhos HTTP que fazem o navegador baixar o arquivo.
    /// </summary>
    private sealed class Planilha : IDisposable
    {
        private readonly MemoryStream _stream;

        public Planilha(FileContentResult arquivo)
        {
            Assert.Equal(ContentTypeXlsx, arquivo.ContentType);
            Assert.NotNull(arquivo.FileDownloadName);
            Assert.EndsWith(".xlsx", arquivo.FileDownloadName);

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

    /// <summary>Concatena as celulas de uma linha - acha marca/legenda sem fixar a coluna.</summary>
    private static string TextoDaLinha(IXLWorksheet aba, int linha)
    {
        var sb = new StringBuilder();
        for (var col = 1; col <= 12; col++)
        {
            sb.Append(aba.Cell(linha, col).GetString()).Append('|');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Linha onde esta o produto com este nome (coluna A). Falhar aqui significa
    /// "o produto nao saiu na planilha", que e o defeito a detectar.
    /// </summary>
    private static int LinhaDoProduto(IXLWorksheet aba, string nome)
    {
        var ultima = UltimaLinha(aba);
        for (var linha = PrimeiraLinhaDados; linha <= ultima; linha++)
        {
            if (aba.Cell(linha, 1).GetString() == nome)
            {
                return linha;
            }
        }

        throw new InvalidOperationException($"Produto '{nome}' nao aparece na planilha.");
    }

    /// <summary>Ultima linha usada da aba (normalmente a linha de totais).</summary>
    private static int UltimaLinha(IXLWorksheet aba) =>
        aba.RangeUsed()?.LastRow().RowNumber() ?? LinhaCabecalho;

    /// <summary>
    /// Primeira celula da coluna cuja formula contem <paramref name="trecho"/>
    /// (ex.: "SUM("). Os totais sao localizados pela FORMULA, nao pela posicao.
    /// </summary>
    private static IXLCell? AcharFormula(IXLWorksheet aba, int coluna, string trecho)
    {
        var ultima = UltimaLinha(aba);
        for (var linha = PrimeiraLinhaDados; linha <= ultima; linha++)
        {
            var celula = aba.Cell(linha, coluna);
            if (celula.HasFormula && celula.FormulaA1.ToUpperInvariant().Contains(trecho))
            {
                return celula;
            }
        }

        return null;
    }

    /// <summary>Configuracao em memoria - o mesmo contrato de appsettings.json, sem tocar em arquivo.</summary>
    private static IConfiguration Config(params (string Chave, string Valor)[] pares)
    {
        var dados = pares.ToDictionary(p => p.Chave, p => (string?)p.Valor);
        return new ConfigurationBuilder().AddInMemoryCollection(dados).Build();
    }

    /// <summary>Todo o texto da aba - para as buscas de "isto NAO pode aparecer".</summary>
    private static string TextoDaAba(IXLWorksheet aba)
    {
        var ultima = UltimaLinha(aba);
        var sb = new StringBuilder();
        for (var linha = 1; linha <= ultima; linha++)
        {
            sb.Append(TextoDaLinha(aba, linha)).Append('\n');
        }

        return sb.ToString();
    }

    // =====================================================================
    // ESTOQUE - estrutura e filtro (a planilha tem que ser a tela)
    // =====================================================================

    // Aba propria + os 8 titulos literais na linha 4: e o contrato que o dono le.
    [Fact]
    public void ExportarEstoqueExcel_DeveTerAbaEstoqueComOsCabecalhosNaLinha4()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context);

        using var planilha = new Planilha((FileContentResult)controller.ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");

        for (var i = 0; i < CabecalhoEstoque.Length; i++)
        {
            Assert.Equal(CabecalhoEstoque[i], aba.Cell(LinhaCabecalho, i + 1).GetString());
        }
    }

    // Uma linha por produto ATIVO, em ordem alfabetica (a mesma do CSV), e o
    // soft delete (Ativo=false) continua fora do arquivo.
    [Fact]
    public void ExportarEstoqueExcel_DeveTerUmaLinhaPorProdutoAtivoOrdenadoPorNome()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "ZEBRA", preco: 1m, quantidade: 1m);
        qa.SeedProduto(nome: "ARROZ", preco: 2m, quantidade: 2m);
        var inativo = qa.SeedProduto(nome: "PRODUTO ANTIGO", preco: 3m, quantidade: 3m);
        inativo.Ativo = false;
        qa.Context.SaveChanges();

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");

        Assert.Equal("ARROZ", aba.Cell(PrimeiraLinhaDados, 1).GetString());
        Assert.Equal("ZEBRA", aba.Cell(PrimeiraLinhaDados + 1, 1).GetString());

        // Duas linhas de dado + a linha de totais.
        Assert.Equal(PrimeiraLinhaDados + 2, UltimaLinha(aba));

        Assert.DoesNotContain("PRODUTO ANTIGO", TextoDaAba(aba));
    }

    // REGRA DE OURO: busca "leite" na tela -> a planilha tem SO o leite.
    [Fact]
    public void ExportarEstoqueExcel_ComBusca_DeveExportarSomenteOFiltrado()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);
        qa.SeedProduto(nome: "ACUCAR REFINADO", preco: 4.89m, quantidade: 90m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel("leite"));
        var aba = planilha.Aba("Estoque");
        var texto = TextoDaAba(aba);

        Assert.Contains("LEITE INTEGRAL", texto);
        Assert.DoesNotContain("ACUCAR REFINADO", texto);

        // O filtro fica escrito na legenda (linha 2): quem abrir o arquivo
        // depois sabe que ele NAO e o estoque inteiro.
        Assert.Contains("leite", TextoDaLinha(aba, LinhaLegenda), StringComparison.OrdinalIgnoreCase);
    }

    // Sem busca: vem o estoque todo, e nao uma planilha vazia.
    [Fact]
    public void ExportarEstoqueExcel_SemBusca_DeveExportarTodosOsAtivos()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);
        qa.SeedProduto(nome: "ACUCAR REFINADO", preco: 4.89m, quantidade: 90m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var texto = TextoDaAba(planilha.Aba("Estoque"));

        Assert.Contains("LEITE INTEGRAL", texto);
        Assert.Contains("ACUCAR REFINADO", texto);
    }

    // =====================================================================
    // ESTOQUE - TIPO das celulas (a razao de existir do .xlsx)
    // =====================================================================

    // PRECO: numero de VERDADE com formato de real. Se saisse como texto, a
    // planilha nao somaria - que e exatamente o defeito que o Excel veio corrigir.
    [Fact]
    public void ExportarEstoqueExcel_PrecoDeveSerNumeroComFormatoDeReal()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");
        var preco = aba.Cell(LinhaDoProduto(aba, "LEITE INTEGRAL"), 3);

        Assert.Equal(XLDataType.Number, preco.DataType);
        Assert.Equal(5.99d, preco.GetDouble(), 2);
        Assert.False(preco.HasFormula);
        Assert.Contains("R$", preco.Style.NumberFormat.Format);
    }

    // QUANTIDADE tambem e NUMERO (nunca texto): e o numero que o dono usa para
    // conferir reposicao. Produto por peso (0,125 kg) sobrevive com as 3 casas.
    [Fact]
    public void ExportarEstoqueExcel_QuantidadeDeveSerNumero()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "ARROZ", preco: 5m, quantidade: 40m);
        qa.SeedProduto(nome: "BANANA", preco: 3m, quantidade: 0.125m, tipo: TipoProduto.PorPeso);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");

        var inteira = aba.Cell(LinhaDoProduto(aba, "ARROZ"), 4);
        Assert.Equal(XLDataType.Number, inteira.DataType);
        Assert.Equal(40d, inteira.GetDouble(), 3);

        var fracionada = aba.Cell(LinhaDoProduto(aba, "BANANA"), 4);
        Assert.Equal(XLDataType.Number, fracionada.DataType);
        Assert.Equal(0.125d, fracionada.GetDouble(), 3);
    }

    // VALOR EM ESTOQUE: formula REAL (Preco x Estoque), nao numero congelado -
    // assim o dono corrige o estoque na planilha e ve o total acompanhar.
    [Fact]
    public void ExportarEstoqueExcel_ValorEmEstoqueDeveSerFormulaPrecoVezesEstoque()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5m, quantidade: 4m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");
        var linha = LinhaDoProduto(aba, "LEITE INTEGRAL");
        var valor = aba.Cell(linha, 7);

        Assert.True(valor.HasFormula, "Valor em estoque deve ser FORMULA, nao numero congelado.");
        // Formula COMPLETA, nao substrings soltas: com a coluna Tipo fora do
        // layout, o valor virou C (Preco) x D (Estoque). Comparar a formula
        // inteira mata o falso verde de duas assercoes que se sobreporiam.
        Assert.Equal($"C{linha}*D{linha}", valor.FormulaA1);
        Assert.Equal(20d, valor.Value.GetNumber(), 2);
    }

    // ESTOQUE BAIXO: a linha inteira em vermelho quando esta abaixo do minimo, e
    // NADA quando esta acima.
    [Fact]
    public void ExportarEstoqueExcel_EstoqueBaixoDeveDestacarSomenteAbaixoDoMinimo()
    {
        using var qa = new QaContext();
        // LEITE: minimo 10 e estoque 4 -> abaixo. ACUCAR: estoque 90 -> acima.
        qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 4m, estoqueMinimo: 10m);
        qa.SeedProduto(nome: "ACUCAR", preco: 4.89m, quantidade: 90m, estoqueMinimo: 10m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");
        var alerta = XLColor.FromHtml("#FDE2E2");

        var leite = aba.Cell(LinhaDoProduto(aba, "LEITE"), 1);
        Assert.Equal(alerta, leite.Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#9B1C1C"), leite.Style.Font.FontColor);

        var acucar = aba.Cell(LinhaDoProduto(aba, "ACUCAR"), 1);
        Assert.NotEqual(alerta, acucar.Style.Fill.BackgroundColor);
    }

    // REGRA 11 do projeto: estoque minimo NULO ou ZERO nunca gera alerta. O
    // gerador usa Produto.EstoqueAbaixoDoMinimo, entao este teste protege a
    // regra de ser reimplementada (e errada) na planilha.
    [Fact]
    public void ExportarEstoqueExcel_MinimoNuloOuZeroNaoDeveDestacar()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "SEM MINIMO", preco: 1m, quantidade: 1m, estoqueMinimo: null);
        qa.SeedProduto(nome: "MINIMO ZERO", preco: 1m, quantidade: 1m, estoqueMinimo: 0m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");
        var alerta = XLColor.FromHtml("#FDE2E2");

        Assert.NotEqual(alerta, aba.Cell(LinhaDoProduto(aba, "SEM MINIMO"), 1).Style.Fill.BackgroundColor);
        Assert.NotEqual(alerta, aba.Cell(LinhaDoProduto(aba, "MINIMO ZERO"), 1).Style.Fill.BackgroundColor);
    }

    // =====================================================================
    // INJECAO (seguranca) - o teste mais importante do arquivo
    // =====================================================================

    // Nome que comeca com = + - @ nao pode virar FORMULA na planilha de quem
    // abrir o arquivo: aqui NAO basta "o texto saiu no arquivo", porque o Excel
    // executa a celula. O texto fica gravado como TEXTO e o apostrofo de
    // protecao vira quotePrefix - exatamente o que o Excel faz quando se digita
    // "'=1+1" na mao.
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A9)")]
    [InlineData("-2+3")]
    [InlineData("@HYPERLINK(\"http://ruim\")")]
    public void ExportarEstoqueExcel_NomeMaliciosoNaoDeveVirarFormula(string nome)
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: nome, preco: 1m, quantidade: 1m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");
        var celula = aba.Cell(LinhaDoProduto(aba, nome), 1);

        Assert.False(celula.HasFormula, $"O nome '{nome}' virou FORMULA na planilha.");
        Assert.Equal(XLDataType.Text, celula.DataType);
        Assert.True(celula.Style.IncludeQuotePrefix, "O apostrofo de protecao nao foi aplicado ao nome.");
    }

    // Do outro lado: nome comum NAO pode ganhar apostrofo - se todos ganhassem,
    // a planilha inteira viraria lixo visual.
    [Fact]
    public void ExportarEstoqueExcel_NomeComumNaoDeveGanharApostrofo()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 1m, quantidade: 1m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");
        var celula = aba.Cell(LinhaDoProduto(aba, "LEITE INTEGRAL"), 1);

        Assert.Equal("LEITE INTEGRAL", celula.GetString());
        Assert.False(celula.Style.IncludeQuotePrefix);
    }

    // =====================================================================
    // ESTOQUE - totais, nome do arquivo e o caso vazio
    // =====================================================================

    // Totais: formula de SUM de verdade (2 x 5 + 3 x 4 = 22), nao um numero
    // escrito a mao que deixaria de acompanhar a planilha.
    [Fact]
    public void ExportarEstoqueExcel_DeveTerLinhaDeTotaisComFormulaSoma()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "ARROZ", preco: 5m, quantidade: 2m);
        qa.SeedProduto(nome: "FEIJAO", preco: 4m, quantidade: 3m);

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");

        var total = AcharFormula(aba, 7, "SUM(");
        Assert.NotNull(total);
        Assert.Equal(22d, total!.Value.GetNumber(), 2);
    }

    // Nome do arquivo com a data: quem baixa cinco exports nao precisa abrir
    // cada um para saber de que dia era.
    [Fact]
    public void ExportarEstoqueExcel_DeveNomearArquivoComAData()
    {
        using var qa = new QaContext();

        var arquivo = (FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null);

        Assert.Equal($"estoque-{DateTime.Today:yyyy-MM-dd}.xlsx", arquivo.FileDownloadName);
    }

    // Sem produto nenhum o arquivo ainda tem que ser um .xlsx VALIDO com
    // cabecalho - um arquivo de 0 bytes o Excel recusa abrir.
    [Fact]
    public void ExportarEstoqueExcel_SemProdutos_DeveGerarArquivoValidoComCabecalho()
    {
        using var qa = new QaContext();

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));
        var aba = planilha.Aba("Estoque");

        for (var i = 0; i < CabecalhoEstoque.Length; i++)
        {
            Assert.Equal(CabecalhoEstoque[i], aba.Cell(LinhaCabecalho, i + 1).GetString());
        }
    }

    // =====================================================================
    // HISTORICO DE VENDAS - a parte financeira (PodeVerFinanceiro)
    // =====================================================================

    [Fact]
    public async Task ExportarHistoricoExcel_DeveTerAbaVendasComOsCabecalhosNaLinha4()
    {
        using var qa = new QaContext();
        qa.SeedVenda(total: 50m);

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));
        var aba = planilha.Aba("Vendas");

        for (var i = 0; i < CabecalhoVendas.Length; i++)
        {
            Assert.Equal(CabecalhoVendas[i], aba.Cell(LinhaCabecalho, i + 1).GetString());
        }
    }

    // Filtro de dia: a planilha tem SO o dia pedido. E o teste que impede o dono
    // de fechar o mes com o total de outro dia.
    [Fact]
    public async Task ExportarHistoricoExcel_ComFiltroDeDia_DeveExportarSomenteEsseDia()
    {
        using var qa = new QaContext();
        var alvo = new DateTime(2026, 2, 10, 14, 30, 0);
        qa.SeedVenda(total: 50m, data: alvo);
        qa.SeedVenda(total: 99m, data: alvo.AddDays(-1));

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(alvo));
        var aba = planilha.Aba("Vendas");

        Assert.Equal(50d, aba.Cell(PrimeiraLinhaDados, 9).GetDouble(), 2);

        // Uma venda so: a linha seguinte ja e a de totais.
        Assert.Equal(PrimeiraLinhaDados + 1, UltimaLinha(aba));

        Assert.DoesNotContain("99", TextoDaAba(aba));
    }

    // Sem filtro: vem tudo. A ordem exata nao e fixada de proposito - duas
    // vendas criadas no mesmo instante empatam por DataVenda, e prender o teste
    // a um desempate seria testar o banco, nao a planilha.
    [Fact]
    public async Task ExportarHistoricoExcel_SemFiltro_DeveExportarTodasAsVendas()
    {
        using var qa = new QaContext();
        qa.SeedVenda(total: 50m);
        qa.SeedVenda(total: 70m);

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));
        var aba = planilha.Aba("Vendas");

        var valores = new[]
        {
            aba.Cell(PrimeiraLinhaDados, 9).GetDouble(),
            aba.Cell(PrimeiraLinhaDados + 1, 9).GetDouble()
        };

        Assert.Contains(50d, valores);
        Assert.Contains(70d, valores);

        // Duas linhas de dado + a de totais.
        Assert.Equal(PrimeiraLinhaDados + 2, UltimaLinha(aba));
    }

    // O total vai como esta GRAVADO no caixa, sem recalcular a partir dos itens:
    // se o total for ajustado a mao na tela, a planilha mostra o fechamento real.
    // Aqui os itens somam 1,00 e o total gravado e 42,42.
    [Fact]
    public async Task ExportarHistoricoExcel_TotalDeveSerOGravadoNaVenda()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "ARROZ", preco: 1m, quantidade: 100m);
        var venda = qa.SeedVenda(total: 42.42m);
        venda.Itens.Add(new VendaItem { ProdutoId = produto.Id, Quantidade = 1m, PrecoUnitario = 1m });
        await qa.Context.SaveChangesAsync();

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));
        var aba = planilha.Aba("Vendas");

        Assert.Equal(42.42d, aba.Cell(PrimeiraLinhaDados, 9).GetDouble(), 2);
    }

    // Data como DateTime DE VERDADE (nao texto): e o que permite ao dono
    // ordenar e filtrar por periodo dentro do Excel.
    [Fact]
    public async Task ExportarHistoricoExcel_DataDeveSerDateTime()
    {
        using var qa = new QaContext();
        var quando = new DateTime(2026, 2, 10, 14, 30, 0);
        qa.SeedVenda(total: 50m, data: quando);

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));
        var aba = planilha.Aba("Vendas");
        var data = aba.Cell(PrimeiraLinhaDados, 2);

        Assert.Equal(XLDataType.DateTime, data.DataType);
        Assert.Equal(quando, data.GetDateTime());
    }

    // Totais ao fim: SUM da coluna Total (R$) e a contagem de vendas.
    [Fact]
    public async Task ExportarHistoricoExcel_DeveTerTotaisComContagemESoma()
    {
        using var qa = new QaContext();
        qa.SeedVenda(total: 50m);
        qa.SeedVenda(total: 70m);

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));
        var aba = planilha.Aba("Vendas");

        var soma = AcharFormula(aba, 9, "SUM(");
        Assert.NotNull(soma);
        Assert.Equal(120d, soma!.Value.GetNumber(), 2);

        // Contagem de vendas: por formula de COUNT (o esperado) ou como numero
        // simples na linha de totais - o que nao pode e faltar.
        var linhaTotais = UltimaLinha(aba);
        var achouContagem = false;
        for (var col = 1; col <= 9 && !achouContagem; col++)
        {
            var celula = aba.Cell(linhaTotais, col);
            if (celula.HasFormula && celula.FormulaA1.ToUpperInvariant().Contains("COUNT"))
            {
                achouContagem = true;
            }
            else if (celula.DataType == XLDataType.Number && celula.GetDouble() == 2d)
            {
                achouContagem = true;
            }
        }

        Assert.True(achouContagem, "A linha de totais nao traz a contagem de vendas.");
    }

    // Nome do arquivo leva o dia filtrado (ou "completo"): quem baixa cinco
    // exportacoes nao precisa abrir cada uma para saber de que periodo e.
    [Fact]
    public async Task ExportarHistoricoExcel_DeveNomearArquivoComODiaFiltrado()
    {
        using var qa = new QaContext();
        var alvo = new DateTime(2026, 2, 10);

        var arquivo = (FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(alvo);

        Assert.Equal("vendas-2026-02-10.xlsx", arquivo.FileDownloadName);
    }

    [Fact]
    public async Task ExportarHistoricoExcel_SemFiltro_DeveNomearArquivoCompleto()
    {
        using var qa = new QaContext();

        var arquivo = (FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null);

        Assert.Equal("vendas-completo.xlsx", arquivo.FileDownloadName);
    }

    // Sem venda nenhuma o arquivo ainda sai valido, com cabecalho.
    [Fact]
    public async Task ExportarHistoricoExcel_SemVendas_DeveGerarArquivoValidoComCabecalho()
    {
        using var qa = new QaContext();

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));
        var aba = planilha.Aba("Vendas");

        for (var i = 0; i < CabecalhoVendas.Length; i++)
        {
            Assert.Equal(CabecalhoVendas[i], aba.Cell(LinhaCabecalho, i + 1).GetString());
        }
    }

    // =====================================================================
    // MARCA / LOJA (configuracao) - o cabecalho do arquivo
    // =====================================================================

    // Sem configuracao (o caso da suite e de qualquer ambiente sem appsettings)
    // a marca cai no padrao ControlHub - o arquivo nunca sai sem identificacao.
    [Fact]
    public void ExportarEstoqueExcel_DeveUsarControlHubComoMarcaPadrao()
    {
        using var qa = new QaContext();
        qa.SeedProduto();

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context).ExportarEstoqueExcel(null));

        Assert.Contains("ControlHub", TextoDaLinha(planilha.Aba("Estoque"), LinhaMarca));
    }

    // Loja preenchida aparece JUNTO da marca na linha 1. A comparacao e no texto
    // da linha inteira para nao prender o teste a uma coluna especifica.
    [Fact]
    public void ExportarEstoqueExcel_ComLojaConfigurada_DeveMostrarLojaJuntoDaMarca()
    {
        using var qa = new QaContext();
        qa.SeedProduto();
        var config = Config(("Marca:Nome", "Mercado Central"), ("Loja:Nome", "Matriz"));

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context, config).ExportarEstoqueExcel(null));
        var linha1 = TextoDaLinha(planilha.Aba("Estoque"), LinhaMarca);

        Assert.Contains("Mercado Central", linha1);
        Assert.Contains("Matriz", linha1);
        Assert.DoesNotContain("ControlHub", linha1);
    }

    // Chave presente mas VAZIA (o appsettings.json que vem no repo tem
    // Loja:Nome = "") conta como ausente: nao pode virar um titulo vazio.
    [Fact]
    public void ExportarEstoqueExcel_ComLojaVazia_NaoDeveMostrarTituloVazio()
    {
        using var qa = new QaContext();
        qa.SeedProduto();
        var config = Config(("Marca:Nome", "Mercado Central"), ("Loja:Nome", "   "));

        using var planilha = new Planilha((FileContentResult)new HomeController(qa.Context, config).ExportarEstoqueExcel(null));
        var linha1 = TextoDaLinha(planilha.Aba("Estoque"), LinhaMarca);

        Assert.Contains("Mercado Central", linha1);
        Assert.DoesNotContain("|   |", linha1);
    }

    [Fact]
    public async Task ExportarHistoricoExcel_DeveUsarControlHubComoMarcaPadrao()
    {
        using var qa = new QaContext();

        using var planilha = new Planilha((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoExcel(null));

        Assert.Contains("ControlHub", TextoDaLinha(planilha.Aba("Vendas"), LinhaMarca));
    }

    // =====================================================================
    // AUTORIZACAO (reflexao) - as actions novas nao podem abrir porta nova
    // =====================================================================

    // Autorizacao das actions NOVAS: [HttpGet] e a MESMA policy da action CSV.
    // As duas policies sao comparadas por REFLEXAO (nao por literal copiado):
    // duas portas de saida para o mesmo dado nao podem ter niveis de acesso
    // diferentes.
    [Theory]
    [InlineData(typeof(HomeController), "ExportarEstoqueExcel", "ExportarEstoqueCsv")]
    [InlineData(typeof(CaixaController), "ExportarHistoricoExcel", "ExportarHistoricoCsv")]
    public void ActionsDeExcel_DevemSerHttpGetComAMesmaPolicyDaActionCsv(Type controller, string actionExcel, string actionCsv)
    {
        var excel = controller.GetMethod(actionExcel, BindingFlags.Public | BindingFlags.Instance);
        var csv = controller.GetMethod(actionCsv, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(excel);
        Assert.NotNull(csv);
        Assert.NotNull(excel!.GetCustomAttribute<HttpGetAttribute>());

        var policyExcel = excel.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
        var policyCsv = csv!.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

        Assert.NotNull(policyExcel);
        Assert.Equal(policyCsv, policyExcel);
    }

    // Eco dos literais: alem de iguais, sao as policies que o projeto usa para
    // estoque e para financeiro (as duas "Dono apenas").
    [Fact]
    public void ActionsDeExcel_DevemUsarAsPoliciesEsperadas()
    {
        var estoque = typeof(HomeController)
            .GetMethod("ExportarEstoqueExcel")!
            .GetCustomAttribute<AuthorizeAttribute>()!.Policy;
        var historico = typeof(CaixaController)
            .GetMethod("ExportarHistoricoExcel")!
            .GetCustomAttribute<AuthorizeAttribute>()!.Policy;

        Assert.Equal(Policies.PodeGerenciarEstoque, estoque);
        Assert.Equal(Policies.PodeVerFinanceiro, historico);
    }

}

