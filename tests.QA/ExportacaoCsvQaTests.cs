using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace tests.QA;

/// <summary>
/// FASE A - ITEM 3: Exportacao CSV (estoque e historico de vendas).
///
/// O export e so LEITURA: nao grava, nao recalcula total, nao mexe no caixa.
/// Por isso nao existe migration para ele.
///
/// O teste mais importante deste arquivo e o de CSV INJECTION: um nome de
/// produto como "=1+1" viraria FORMULA no Excel de quem abriu o arquivo.
/// </summary>
public class ExportacaoCsvQaTests
{
    // =====================================================================
    // CSV INJECTION (seguranca) - o teste mais importante do arquivo
    // =====================================================================

    // Produto chamado "=HYPERLINK(...)" nao pode chegar cru ao arquivo: com o
    // apostrofo na frente, o Excel mostra o texto e NAO executa nada.
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A9)")]
    [InlineData("-2+3")]
    [InlineData("@HYPERLINK(\"http://x\")")]
    public async Task EscaparTexto_ComInicioDeFormula_DeviaNeutralizar(string nome)
    {
        Assert.StartsWith("'", ExportacaoCsv.EscaparTexto(nome));
    }

    // Espaco na frente e o truque classico para furar o filtro: "=1+1" com
    // espaco continua sendo formula para o Excel, entao precisa ser neutralizado
    // tambem.
    [Fact]
    public async Task EscaparTexto_ComEspacoAntesDaFormula_DeviaNeutralizar()
    {
        Assert.StartsWith("'", ExportacaoCsv.EscaparTexto("  =1+1"));
    }

    // Escudo do lado de dentro da view: um produto malicioso tem de sair
    // neutralizado no arquivo de verdade, nao so no helper isolado.
    [Fact]
    public async Task ExportarEstoque_ComProdutoMalicioso_DeveNeutralizarNoArquivo()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "=HYPERLINK(\"http://ruim\")", preco: 1m, quantidade: 1m);
        var controller = new HomeController(qa.Context);

        var arquivo = (FileContentResult)controller.ExportarEstoqueCsv(null);
        var texto = LerTexto(arquivo);

        // O nome precisa estar protegido com o apostrofo de abertura.
        Assert.Contains("'=HYPERLINK", texto);
    }

    // O mesmo texto SEM inicio perigoso nao pode ser alterado: se tudo
    // ganhasse apostrofo, o CSV inteiro viraria lixo visual.
    [Fact]
    public async Task EscaparTexto_TextoComum_DeveFicarIntacto()
    {
        Assert.Equal("LEITE INTEGRAL", ExportacaoCsv.EscaparTexto("LEITE INTEGRAL"));
        Assert.Equal("Arroz, tipo 1", ExportacaoCsv.EscaparTexto("Arroz, tipo 1"));
        Assert.Equal(string.Empty, ExportacaoCsv.EscaparTexto(null));
    }

    // =====================================================================
    // O QUE O EXCEL PRECISA PARA ABRIR O ARQUIVO CERTO
    // =====================================================================

    // BOM UTF-8 no comeco: sem ele o Excel do Windows abre como latin-1 e todo
    // acento vira "Leite Integral??". O teste olha os 3 bytes do Preamble.
    [Fact]
    public async Task Montar_DeveGravarBomUtf8NoComecoDoArquivo()
    {
        var arquivo = ExportacaoCsv.Montar(new[] { "Produto" }, Array.Empty<IReadOnlyList<string>>());

        Assert.True(arquivo.Length >= 3);
        Assert.Equal(0xEF, arquivo[0]);
        Assert.Equal(0xBB, arquivo[1]);
        Assert.Equal(0xBF, arquivo[2]);
    }

    // Separador ';': e o que o Excel pt-BR espera. Com ',' um preco "5,99"
    // quebraria a coluna em duas.
    [Fact]
    public async Task Montar_DeveUsarPontoEVirgulaComoSeparador()
    {
        var arquivo = ExportacaoCsv.Montar(
            new[] { "Produto", "Preco" },
            new[] { (IReadOnlyList<string>)new[] { "LEITE", "5,99" } });

        var texto = Encoding.UTF8.GetString(arquivo, 3, arquivo.Length - 3);
        Assert.Contains("\"LEITE\";\"5,99\"", texto);
    }

    // Numero pt-BR: milhar com ponto e decimal com virgula. Um "1,234.50"
    // (padrao en-US) faria o Excel ler errado.
    [Fact]
    public async Task Dinheiro_DeveUsarFormatoPtBr()
    {
        Assert.Equal("1.234,50", ExportacaoCsv.Dinheiro(1234.5m));
        Assert.Equal("5,99", ExportacaoCsv.Dinheiro(5.99m));
        Assert.Equal("0,00", ExportacaoCsv.Dinheiro(0m));
    }

    // Milhar grande: 1.234.567,00. E o numero que o dono compara de mes para
    // mes, entao precisa sair sempre igual - por isso o agrupamento e feito a
    // mao no helper, e nao delegated ao formatador do SO.
    [Fact]
    public async Task Dinheiro_ComMilhar_DeveAgruparDeTresEmTres()
    {
        Assert.Equal("1.234.567,00", ExportacaoCsv.Dinheiro(1234567m));
        Assert.Equal("999,99", ExportacaoCsv.Dinheiro(999.99m));
        Assert.Equal("1.000,00", ExportacaoCsv.Dinheiro(1000m));
    }

    // Valor negativo: e o caso do estorno/sangria no historico. Precisa sair
    // "-5,00" (e nao "5,00" nem "-5,00" virando coluna), para o Excel somar
    // certo na coluna de total.
    [Fact]
    public async Task Dinheiro_Negativo_DeveManterOSinal()
    {
        Assert.Equal("-5,00", ExportacaoCsv.Dinheiro(-5m));
        Assert.Equal("-1.234,50", ExportacaoCsv.Dinheiro(-1234.5m));
    }

    // Quantidade: 3 casas para o produto por peso (0,125 kg), sem zeros
    // sobrando para o inteiro.
    [Fact]
    public async Task Quantidade_DeveAceitarAteTresCasas()
    {
        Assert.Equal("0,125", ExportacaoCsv.Quantidade(0.125m));
        Assert.Equal("40", ExportacaoCsv.Quantidade(40m));
        Assert.Equal("12,5", ExportacaoCsv.Quantidade(12.5m));
    }

    // Quantidade alta NAO leva separador de milhar: "1.234" seria lido pelo
    // Excel como TEXTO (ele so entende milhar em numero com casas decimais),
    // e a coluna de estoque pararia de somar. Trava esse comportamento.
    [Fact]
    public async Task Quantidade_ComMilhar_DeveFicarSemPontoDeMilhar()
    {
        Assert.Equal("1234", ExportacaoCsv.Quantidade(1234m));
        Assert.Equal("12345", ExportacaoCsv.Quantidade(12345m));
    }

    // Data no formato brasileiro, sem hora quando a hora e 00:00 (que e o caso
    // do filtro por dia, onde o dono so quer saber o dia).
    [Fact]
    public async Task DataHora_DeveUsarFormatoBrasileiro()
    {
        Assert.Equal("10/02/2026 14:30", ExportacaoCsv.DataHora(new DateTime(2026, 2, 10, 14, 30, 0)));
        Assert.Equal("10/02/2026", ExportacaoCsv.DataHora(new DateTime(2026, 2, 10)));
    }

    // Aspas e separador DENTRO do texto nao podem criar coluna nova: o
    // "Arroz; Integral" viraria duas colunas e bagunçaria a tabela.
    [Fact]
    public async Task Montar_ComSeparadorDentroDoTexto_DeveManterUmaCelula()
    {
        var arquivo = ExportacaoCsv.Montar(
            new[] { "Produto", "Tipo" },
            new[] { (IReadOnlyList<string>)new[] { "Arroz; Integral 1kg", "Prateleira" } });

        var texto = Encoding.UTF8.GetString(arquivo, 3, arquivo.Length - 3);
        Assert.Contains("\"Arroz; Integral 1kg\"", texto);
        Assert.Contains("\"Prateleira\"", texto);
    }

    // Aspas duplas dentro do valor sao dobradas (regra do RFC 4180).
    [Fact]
    public async Task Montar_ComAspasNoTexto_DeveDobrarAsAspas()
    {
        var arquivo = ExportacaoCsv.Montar(
            new[] { "Produto" },
            new[] { (IReadOnlyList<string>)new[] { "LEITE \"1L\"" } });

        var texto = Encoding.UTF8.GetString(arquivo, 3, arquivo.Length - 3);
        Assert.Contains("\"LEITE \"\"1L\"\"\"", texto);
    }

    // =====================================================================
    // REGRA DE OURO: O CSV TEM QUE SER A TELHA (E O FILTRO DELA)
    // =====================================================================

    // Busca "leite" na tela -> o CSV tem SÓ o leite. Sem isso o dono receberia
    // a planilha inteira e fecharia o estoque pelo numero errado.
    [Fact]
    public async Task ExportarEstoque_ComBusca_DeveExportarSomenteOFiltrado()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);
        qa.SeedProduto(nome: "ACUCAR REFINADO", preco: 4.89m, quantidade: 90m);
        var controller = new HomeController(qa.Context);

        var texto = LerTexto((FileContentResult)controller.ExportarEstoqueCsv("leite"));

        Assert.Contains("LEITE INTEGRAL", texto);
        Assert.DoesNotContain("ACUCAR REFINADO", texto);
    }

    // Sem busca: vem o estoque todo, e nao uma linha vazia.
    [Fact]
    public async Task ExportarEstoque_SemBusca_DeveExportarTodosOsAtivos()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);
        qa.SeedProduto(nome: "ACUCAR REFINADO", preco: 4.89m, quantidade: 90m);
        var controller = new HomeController(qa.Context);

        var texto = LerTexto((FileContentResult)controller.ExportarEstoqueCsv(null));

        Assert.Contains("LEITE INTEGRAL", texto);
        Assert.Contains("ACUCAR REFINADO", texto);
    }

    // Produto desativado (soft delete) nao aparece na tela, entao tambem nao
    // pode aparecer no arquivo.
    [Fact]
    public async Task ExportarEstoque_NaoDeveIncluirProdutoDesativado()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "LEITE INTEGRAL", preco: 5.99m, quantidade: 40m);
        var inativo = qa.SeedProduto(nome: "PRODUTO ANTIGO", preco: 1m, quantidade: 5m);
        inativo.Ativo = false;
        qa.Context.SaveChanges();

        var texto = LerTexto((FileContentResult)new HomeController(qa.Context).ExportarEstoqueCsv(null));

        Assert.DoesNotContain("PRODUTO ANTIGO", texto);
    }

    // O estoque minimo e o alerta do item 1 viajam no arquivo: e o que o dono
    // usa para decidir a reposicao sem volver ao sistema.
    [Fact]
    public async Task ExportarEstoque_DeveTrazerEstoqueMinimoEOSinalDeAlerta()
    {
        using var qa = new QaContext();
        // Leite: minimo 10 e estoque 4 -> abaixo do minimo (SIM).
        // Acucar: minimo 10 e estoque 90 -> acima do minimo (NAO).
        qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 4m, estoqueMinimo: 10m);
        qa.SeedProduto(nome: "ACUCAR", preco: 4.89m, quantidade: 90m, estoqueMinimo: 10m);
        var controller = new HomeController(qa.Context);

        var texto = LerTexto((FileContentResult)controller.ExportarEstoqueCsv(null));

        // Cabecalho e valor do minimo precisam estar no arquivo.
        Assert.Contains("Estoque Minimo", texto);
        Assert.Contains("\"10\"", texto);

        // As duas linhas de alerta: o dono filtra por SIM no Excel e ve o que
        // precisa repor.
        var linhas = texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var leite = linhas.Single(l => l.Contains("LEITE"));
        var acucar = linhas.Single(l => l.Contains("ACUCAR"));

        Assert.Contains("\"SIM\"", leite);
        Assert.Contains("\"NAO\"", acucar);
    }

    // Nome do arquivo com a data: quem baixa vira do PDF nao precisa abrir
    // cada um para saber de que dia era.
    [Fact]
    public async Task ExportarEstoque_DeveNomearArquivoComAData()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context);

        var arquivo = (FileContentResult)controller.ExportarEstoqueCsv(null);

        Assert.Equal($"estoque-{DateTime.Today:yyyy-MM-dd}.csv", arquivo.FileDownloadName);
    }

    // Sem produto nenhum, o arquivo precisa sair valido: cabecalho + BOM.
    // Um CSV de 0 bytes faria o Excel reclamar de arquivo corrompido.
    [Fact]
    public async Task ExportarEstoque_SemProdutos_DeveGerarArquivoComCabecalho()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context);

        var texto = LerTexto((FileContentResult)controller.ExportarEstoqueCsv(null));

        Assert.Contains("Produto", texto);
        Assert.Contains("Estoque Minimo", texto);
    }

    // =====================================================================
    // HISTORICO DE VENDAS - a parte financeira (PodeVerFinanceiro)
    // =====================================================================

    // Filtro de dia: o arquivo tem SO o dia pedido. E o teste que impede o
    // dono de fechar o mes com o total de outro dia.
    [Fact]
    public async Task ExportarHistorico_ComFiltroDeDia_DeveExportarSomenteEsseDia()
    {
        using var qa = new QaContext();
        var alvo = new DateTime(2026, 2, 10, 14, 30, 0);
        qa.SeedVenda(total: 50m, data: alvo);
        qa.SeedVenda(total: 99m, data: alvo.AddDays(-1));

        var texto = LerTexto((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoCsv(alvo));

        Assert.Contains("50,00", texto);
        Assert.DoesNotContain("99,00", texto);
    }

    // Sem filtro de dia: vem tudo, com o total de cada venda. A coluna de
    // dinheiro e a que o dono usa para conferir o fechamento.
    [Fact]
    public async Task ExportarHistorico_SemFiltro_DeveExportarTodasAsVendas()
    {
        using var qa = new QaContext();
        qa.SeedVenda(total: 50m);
        qa.SeedVenda(total: 70m);

        var texto = LerTexto((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoCsv(null));

        Assert.Contains("50,00", texto);
        Assert.Contains("70,00", texto);
    }

    // Nome do arquivo leva o dia filtrado, para o dono saber de que periodo e
    // o arquivo so pelo nome.
    [Fact]
    public async Task ExportarHistorico_DeveNomearArquivoComODiaFiltrado()
    {
        using var qa = new QaContext();
        var alvo = new DateTime(2026, 2, 10);

        // Cast para FileContentResult: o metodo e assinado como IActionResult
        // (padrao do projeto), mas o que ele devolve e sempre um download.
        var arquivo = (FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoCsv(alvo);

        Assert.Equal("vendas-2026-02-10.csv", arquivo.FileDownloadName);
    }

    // Sem filtro o nome diz "completo", para nao virar um "vendas-01-01-2026"
    // que parece dia unico.
    [Fact]
    public async Task ExportarHistorico_SemFiltro_DeveNomearArquivoCompleto()
    {
        using var qa = new QaContext();

        var arquivo = (FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoCsv(null);

        Assert.Equal("vendas-completo.csv", arquivo.FileDownloadName);
    }

    // O total vai como esta gravado no caixa, sem recalcular a partir dos
    // itens: se o total for ajustado a mao na tela, o CSV mostra o fechamento
    // real.
    [Fact]
    public async Task ExportarHistorico_DeveUsarOTotalGravadoDaVenda()
    {
        using var qa = new QaContext();
        var venda = qa.SeedVenda(total: 42.42m);

        var texto = LerTexto((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoCsv(null));

        Assert.Contains("42,42", texto);
        Assert.Equal(42.42m, venda.TotalVenda);
    }

    // Sem venda nenhuma, o arquivo ainda sai com cabecalho - um CSV de zero
    // bytes faria o Excel reclamar de arquivo corrompido.
    [Fact]
    public async Task ExportarHistorico_SemVendas_DeveGerarArquivoComCabecalho()
    {
        using var qa = new QaContext();

        var texto = LerTexto((FileContentResult)await new CaixaController(qa.Context).ExportarHistoricoCsv(null));

        Assert.Contains("Metodo Pagamento", texto);
        Assert.Contains("Total (R$)", texto);
    }

    /// <summary>
    /// Decodifica o arquivo exportado pulando o BOM UTF-8 e checa o cabecalhos
    /// HTTP que fazem o navegador BAIXAR o csv em vez de tentar abrir na aba.
    /// </summary>
    private static string LerTexto(FileContentResult arquivo)
    {
        Assert.Equal("text/csv; charset=utf-8", arquivo.ContentType);

        // Sem Content-Disposition o navegador ABRE o csv na aba em vez de baixar.
        Assert.NotNull(arquivo.FileDownloadName);

        return Encoding.UTF8.GetString(arquivo.FileContents, 3, arquivo.FileContents.Length - 3);
    }

    // =====================================================================
    // ESTRUTURA DE LINHAS - o bug que so apareceu abrindo o arquivo
    // =====================================================================

    // Cabecalho tem que ser UMA linha com varios campos. Este teste existe
    // porque o defeito real so apareceu na inspecao visual do arquivo: os
    // titulos saiam um por linha, e o teste antigo (que so procurava o texto
    // "Produto") passava assim mesmo. O Excel abria 6 linhas de cabecalho.
    [Fact]
    public async Task Montar_DeveColocarTodosOsCabecalhosNaMesmaLinha()
    {
        var arquivo = ExportacaoCsv.Montar(
            new[] { "Produto", "Tipo", "Preco (R$)" },
            new[] { (IReadOnlyList<string>)new[] { "LEITE", "Prateleira", "5,99" } });

        var texto = Encoding.UTF8.GetString(arquivo, 3, arquivo.Length - 3);
        var linhas = texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // Linha 1 = cabecalho inteiro. Linha 2 = o unico dado.
        Assert.Equal(2, linhas.Length);
        Assert.Equal("\"Produto\";\"Tipo\";\"Preco (R$)\"", linhas[0]);
        Assert.Equal("\"LEITE\";\"Prateleira\";\"5,99\"", linhas[1]);
    }

    // Nenhuma linha pode comecar com ';': sobrar separador vazio no fim da
    // linha criaria uma coluna fantasma em toda linha.
    [Fact]
    public async Task Montar_NaoDeveDeixarSeparadorVazioNoFimDaLinha()
    {
        var arquivo = ExportacaoCsv.Montar(
            new[] { "Produto", "Preco" },
            new[] { (IReadOnlyList<string>)new[] { "LEITE", "5,99" } });

        var texto = Encoding.UTF8.GetString(arquivo, 3, arquivo.Length - 3);

        foreach (var linha in texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.DoesNotContain(";", linha.Substring(linha.Length - 1));
        }
    }
}
