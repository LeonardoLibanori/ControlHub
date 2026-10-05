using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace tests.QA;

/// <summary>
/// ETAPA A - Dashboard. O QUE O PAINEL PRECISA RESPONDER, escrito antes da
/// implementacao (os testes vermelhos sao o contrato).
///
/// POR QUE "TotalVenda GRAVADO" E NAO A SOMA DOS ITENS: o caixa pode fechar
/// uma venda com o total ajustado a mao (desconto no balcao). Se o dashboard
/// recalcular, mostra um numero que nunca bate com o caixa. Regra de ouro do
/// projeto: dinheiro gravado sai gravado. O teste 6 existe so para travar isso.
///
/// QUE DIA E "HOJE": DateTime.Today com filtro DataVenda.Date == hoje - o
/// MESMO padrao de CaixaController.Historico / ObterStatusAtual /
/// ObterDadosFechamento. Os testes 2 e 3 travam as bordas desse filtro para
/// que o dashboard nunca divirja do relatorio que ja existe.
///
/// POR QUE ESTOQUE BAIXO NAO VAI EM SQL: Produto.EstoqueAbaixoDoMinimo e
/// [NotMapped] porque Quantidade e decimal em TEXT no SQLite e a collation
/// BINARY ordenaria "10" antes de "9". O teste 11 inclui o par 9/10 e 10/9 que
/// quebra em SQL, para ninguem "otimizar" isso depois.
/// </summary>
public class DashboardQaTests
{
    private static DateTime Hoje => DateTime.Today;
    private static DateTime Ontem => DateTime.Today.AddDays(-1);
    private static DateTime Amanha => DateTime.Today.AddDays(1);

    /// <summary>
    /// Roda a action e devolve o view-model. Precisa de await porque Index e
    /// async (os dados vem de consultas ao banco).
    /// </summary>
    private static async Task<DashboardViewModel> ChamarIndex(ApplicationDbContext ctx)
    {
        var controller = new DashboardController(ctx);
        var result = await controller.Index();

        var view = Assert.IsAssignableFrom<ViewResult>(result);
        return Assert.IsType<DashboardViewModel>(view.Model);
    }

    // =====================================================================
    // (1) TOTAL DE HOJE
    // =====================================================================

    [Fact]
    public async Task Dashboard_TotalDeHoje_SomaSoVendasDeHoje()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));   // conta
        qa.SeedVenda(50m, data: Hoje.AddHours(18));    // conta
        qa.SeedVenda(999m, data: Ontem.AddHours(10));  // NAO conta
        qa.SeedVenda(777m, data: Amanha.AddHours(10)); // NAO conta

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(150m, model.TotalVendasHoje);
    }

    // =====================================================================
    // (2) FRONTEIRAS DO DIA
    // =====================================================================

    [Fact]
    public async Task Dashboard_FronteirasDoDia_ContaMeiaNoiteEUltimoTickDeHoje()
    {
        using var qa = new QaContext();
        qa.SeedVenda(10m, data: Hoje);                          // 00:00:00.000
        qa.SeedVenda(20m, data: Hoje.AddDays(1).AddTicks(-1));  // 23:59:59.9999999
        qa.SeedVenda(30m, data: Amanha);                       // 00:00:00 de amanha

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(30m, model.TotalVendasHoje);
    }

    // =====================================================================
    // (3) PARIDADE COM O RELATORIO QUE JA EXISTE
    // =====================================================================

    [Fact]
    public async Task Dashboard_TotalDeHoje_IgualAoHistoricoParaHoje()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(250m, metodoPagamento: "Credito", data: Hoje.AddHours(11));
        qa.SeedVenda(333m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(9));

        var model = await ChamarIndex(qa.Context);

        var controller = new CaixaController(qa.Context);
        var resultado = await controller.Historico(Hoje);
        var listada = ((ViewResult)resultado).Model as IEnumerable<Venda>;

        Assert.NotNull(listada);
        Assert.Equal(listada!.Sum(v => v.TotalVenda), model.TotalVendasHoje);
    }

    // =====================================================================
    // (4) NUMERO DE VENDAS E TICKET MEDIO
    // =====================================================================

    [Fact]
    public async Task Dashboard_ContaVendasDeHoje_EETicketMedio()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(9));
        qa.SeedVenda(200m, data: Hoje.AddHours(10));
        qa.SeedVenda(400m, data: Ontem.AddHours(9));

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(2, model.NumeroVendasHoje);
        Assert.Equal(150m, model.TicketMedio);
    }

    [Fact]
    public async Task Dashboard_SemVendas_RetornaZeroSemEstourar()
    {
        using var qa = new QaContext();

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(0m, model.TotalVendasHoje);
        Assert.Equal(0, model.NumeroVendasHoje);
        Assert.Equal(0m, model.TicketMedio);
    }

    // =====================================================================
    // (5) ARREDONDAMENTO DO TICKET MEDIO
    // =====================================================================

    [Fact]
    public async Task Dashboard_TicketMedio_ArredondaEmDuasCasas()
    {
        using var qa = new QaContext();
        // 0,02 + 0,03 = 0,05 / 2 = 0,025. Math.Round(0.025, 2) com o
        // MidpointRounding padrao (ToEven) daria 0,02; o painel precisa de
        // 0,03 (AwayFromZero), que e o que o dono le no cupom.
        qa.SeedVenda(0.02m, data: Hoje.AddHours(9));
        qa.SeedVenda(0.03m, data: Hoje.AddHours(10));

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(0.03m, model.TicketMedio);
    }

    // =====================================================================
    // (6) USA O TOTAL GRAVADO (REGRA DE OURO DO CAIXA)
    // =====================================================================

    [Fact]
    public async Task Dashboard_UsaTotalVendaGravado_NAORecalculaPelosItens()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "P1", preco: 10m, quantidade: 100m);

        var venda = qa.SeedVenda(7.77m, data: Hoje.AddHours(10));
        // Os itens somam 30,00, mas o caixa gravou 7,77 (desconto no balcao).
        venda.Itens.Add(new VendaItem { ProdutoId = produto.Id, Quantidade = 3m, PrecoUnitario = 10m });
        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(7.77m, model.TotalVendasHoje);
    }

    // =====================================================================
    // (7) TOTAL POR METODO DE PAGAMENTO
    // =====================================================================

    [Fact]
    public async Task Dashboard_TotalPorMetodo_AgrupaSemDiferenciarMaiusculas()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(50m, metodoPagamento: "dinheiro", data: Hoje.AddHours(10));
        qa.SeedVenda(70m, metodoPagamento: "Credito", data: Hoje.AddHours(11));
        qa.SeedVenda(80m, metodoPagamento: "PIX", data: Ontem.AddHours(9));

        var model = await ChamarIndex(qa.Context);

        // "Dinheiro" e "dinheiro" viram UM grupo so. O nome exibido e o PRIMEIRO
        // encontrado (como manda a regra do controller), nao em uppercase.
        var dinheiro = Assert.Single(model.TotalPorMetodo,
            m => string.Equals(m.Metodo, "Dinheiro", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(150m, dinheiro.Total);

        var credito = Assert.Single(model.TotalPorMetodo,
            m => string.Equals(m.Metodo, "Credito", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(70m, credito.Total);

        // Ontem nao entra em nenhum grupo, e PIX nao aparece (so vendeu ontem).
        Assert.Equal(2, model.TotalPorMetodo.Count);

        // E a soma dos grupos bate com o total do dia.
        Assert.Equal(model.TotalVendasHoje, model.TotalPorMetodo.Sum(m => m.Total));
    }

    // =====================================================================
    // (8) GAVETA
    // =====================================================================

    [Fact]
    public async Task Dashboard_Gaveta_IgualAoSaldoDeObterStatusAtual()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(200m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10));
        qa.SeedVenda(900m, metodoPagamento: "Credito", data: Hoje.AddHours(11)); // nao entra
        qa.SeedVenda(800m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(9)); // outro dia
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(12));

        var model = await ChamarIndex(qa.Context);

        var controller = new CaixaController(qa.Context);
        var json = await controller.ObterStatusAtual();
        var valor = ((JsonResult)json).Value!;
        var saldoDoCaixa = Convert.ToDecimal(valor.GetType().GetProperty("saldo")!.GetValue(valor));

        Assert.Equal(saldoDoCaixa, model.SaldoGaveta);
        // 300 (dinheiro de hoje: 100 + 200) + 50 - 20 = 330
        Assert.Equal(330m, model.SaldoGaveta);
    }

    [Fact]
    public async Task Dashboard_Gaveta_IgnoraVendaFechadaDeDinheiro()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(500m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10), isFechado: true);

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(100m, model.SaldoGaveta);
    }

    // =====================================================================
    // (8c) A GAVETA DO DASHBOARD E A MESMA QUE LIMITA A SANGRIA (REGRA 7)
    // =====================================================================

    /// <summary>
    /// PROVA DE QUE OS DOIS NUMEROS SAO O MESMO. A regra 7 diz que a sangria
    /// nao pode passar do saldo em gaveta, e esse saldo vem de
    /// CalcularSaldoEmGaveta. Se o dashboard mostrasse outro numero, o dono
    /// veria "R$ 530,00 na gaveta" e o sistema recusaria a sangria de
    /// R$ 530,01 - dois paineis discordando do mesmo dinheiro.
    ///
    /// Dois bancos separados de proposito: o primeiro muta o saldo (a sangria
    /// bem-sucedida consome dinheiro), entao reutilizar o mesmo banco tornaria
    /// o segundo cenario dependente do primeiro.
    /// </summary>
    [Fact]
    public async Task Dashboard_Gaveta_ComSangriaNoLimite_EEAceitaPelaRegra7()
    {
        // ---- cenario 1: sangria de EXATAMENTE o saldo e aceita ----
        using (var qa = new QaContext())
        {
            qa.SeedVenda(300m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
            qa.SeedVenda(200m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10));
            qa.SeedVenda(900m, metodoPagamento: "Credito", data: Hoje.AddHours(11)); // fora da gaveta
            qa.SeedVenda(800m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(9)); // outro dia
            qa.SeedVenda(700m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(12), isFechado: true); // fechada
            qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
            qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(11));

            var model = await ChamarIndex(qa.Context);
            Assert.Equal(530m, model.SaldoGaveta);

            // O valor chega como TEXTO em formatoInvariant (ex.: "530.00") - um
            // "53000" seria lido como cincoenta e tres mil e recusado.
            var controller = new CaixaController(qa.Context);
            var resultado = controller.RegistrarSangria("530.00", "saque no limite");

            // 530,00 == saldo: a regra 7 NAO pode recusar.
            Assert.IsType<JsonResult>(resultado);
            Assert.True((bool)((JsonResult)resultado).Value!.GetType()
                .GetProperty("success")!.GetValue(((JsonResult)resultado).Value)!);
        }

        // ---- cenario 2: 0,01 acima do saldo e recusado ----
        using (var qa = new QaContext())
        {
            qa.SeedVenda(300m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
            qa.SeedVenda(200m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10));
            qa.SeedVenda(900m, metodoPagamento: "Credito", data: Hoje.AddHours(11));
            qa.SeedVenda(800m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(9));
            qa.SeedVenda(700m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(12), isFechado: true);
            qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
            qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(11));

            var model = await ChamarIndex(qa.Context);
            Assert.Equal(530m, model.SaldoGaveta);

            var controller = new CaixaController(qa.Context);
            var resultado = controller.RegistrarSangria("530.01", "um centavo acima");

            var valor = ((JsonResult)resultado).Value!;
            var success = (bool)valor.GetType().GetProperty("success")!.GetValue(valor)!;
            Assert.False(success,
                "A regra 7 recusaria 530,01 com saldo de 530,00. Se este teste "
                + "falhar, a gaveta do dashboard NAO e a mesma do caixa.");
        }
    }

    // =====================================================================
    // (8d) OBTERSTATUSATUAL x DASHBOARD APOS UMA VENDA FECHADA
    // =====================================================================

    /// <summary>
    /// BUG-28: <c>ObterStatusAtual</c> NAO filtra <c>IsFechado</c>
    /// (CaixaController L321, L325 e L331), enquanto a regra 7
    /// (<c>RegistrarMovimentacaoInterno</c> L263, L268, L273) filtra. Apos o
    /// fechamento do dia, o PDV continua somando as vendas ja fechadas no
    /// saldo e a gaveta do dashboard nao. Os dois numeros divergem.
    ///
    /// Este teste declara o COMPORTAMENTO CORRETO e fica IGNORADO de proposito:
    /// corrigir e trabalho de commit proprio, junto com a quebra visual que a
    /// conferencia de caixa mostra (admin-caixa.js L113).
    /// </summary>
    [Fact]
    public async Task Dashboard_Gaveta_IgualAoStatusAposVendaFechada_ComportamentoEsperado()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(500m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10), isFechado: true);
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(11));

        var model = await ChamarIndex(qa.Context);

        // Gaveta do dashboard: 100 (aberta) + 50 - 20 = 130.
        Assert.Equal(130m, model.SaldoGaveta);

        var controller = new CaixaController(qa.Context);
        var json = await controller.ObterStatusAtual();
        var valor = ((JsonResult)json).Value!;
        var saldo = Convert.ToDecimal(valor.GetType().GetProperty("saldo")!.GetValue(valor));

        // Hoje: 610 (soma a venda FECHADA) + 50 - 20 = 640. Deveria ser 130.
        Assert.Equal(130m, saldo);
    }

    // =====================================================================
    // (9) DEPOIS DO FECHAMENTO
    // =====================================================================

    [Fact]
    public async Task Dashboard_AposFechamento_GavetaZeraEOTotalDeHojeNaoSome()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(200m, metodoPagamento: "Credito", data: Hoje.AddHours(10));
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));

        var antes = await ChamarIndex(qa.Context);
        Assert.Equal(300m, antes.TotalVendasHoje);
        Assert.Equal(150m, antes.SaldoGaveta);

        new CaixaController(qa.Context).ZerarCaixaParaProximoDia();

        var depois = await ChamarIndex(qa.Context);
        Assert.Equal(0m, depois.SaldoGaveta);
        // O total do dia NAO some: a venda continua no historico do dia.
        Assert.Equal(300m, depois.TotalVendasHoje);
    }

    // =====================================================================
    // (10) ULTIMOS 7 DIAS
    // =====================================================================

    [Fact]
    public async Task Dashboard_SerieDe7Dias_Tem7PontosEmOrdemCronologica()
    {
        using var qa = new QaContext();
        qa.SeedVenda(11m, data: Hoje.AddHours(10));            // hoje
        qa.SeedVenda(22m, data: Hoje.AddDays(-1).AddHours(10)); // -1
        qa.SeedVenda(44m, data: Hoje.AddDays(-3).AddHours(10)); // -3
        // Hoje-2 fica sem venda: precisa aparecer com 0.
        qa.SeedVenda(99m, data: Hoje.AddDays(-8).AddHours(10)); // fora da janela

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(7, model.SerieUltimos7Dias.Count);

        // Cronologica: de -6 ate hoje.
        for (var i = 0; i < model.SerieUltimos7Dias.Count; i++)
        {
            Assert.Equal(Hoje.AddDays(-6 + i), model.SerieUltimos7Dias[i].Dia);
        }

        Assert.Equal(11m, model.SerieUltimos7Dias[6].Total);
        Assert.Equal(22m, model.SerieUltimos7Dias[5].Total);
        Assert.Equal(0m, model.SerieUltimos7Dias[4].Total);   // -2 sem venda
        Assert.Equal(44m, model.SerieUltimos7Dias[3].Total);

        Assert.DoesNotContain(model.SerieUltimos7Dias, d => d.Total == 99m);

        var esperado = qa.Context.Vendas
            .Where(v => v.DataVenda.Date >= Hoje.AddDays(-6) && v.DataVenda.Date <= Hoje)
            .Sum(v => v.TotalVenda);
        Assert.Equal(esperado, model.SerieUltimos7Dias.Sum(d => d.Total));
    }

    // =====================================================================
    // (11) ESTOQUE BAIXO
    // =====================================================================

    [Fact]
    public async Task Dashboard_EstoqueBaixo_UsaAPropriedadeDoModeloEIgnoraInativos()
    {
        using var qa = new QaContext();
        // Zero estoque primeiro: e o caso mais critico.
        qa.SeedProduto(nome: "ZEBRA",   quantidade: 0m,  estoqueMinimo: 5m);
        qa.SeedProduto(nome: "ALFA",    quantidade: 3m,  estoqueMinimo: 10m);
        qa.SeedProduto(nome: "MEIO",    quantidade: 10m, estoqueMinimo: 10m); // igual: NAO
        qa.SeedProduto(nome: "SAUDOSO", quantidade: 50m, estoqueMinimo: 10m);
        qa.SeedProduto(nome: "SEMMIN",  quantidade: 1m,  estoqueMinimo: null); // nulo: NAO
        qa.SeedProduto(nome: "MINZERO", quantidade: 0m,  estoqueMinimo: 0m);   // zero: NAO
        // Guarda contra comparacao TEXT: 9 < 10 e 10 > 9.
        qa.SeedProduto(nome: "NOVE",    quantidade: 9m,  estoqueMinimo: 10m);
        qa.SeedProduto(nome: "DEZ",     quantidade: 10m, estoqueMinimo: 9m);

        var inativo = qa.SeedProduto(nome: "INATIVO", quantidade: 0m, estoqueMinimo: 5m);
        inativo.Ativo = false;
        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context);

        // Baixo de verdade: ZEBRA(0<5), ALFA(3<10), NOVE(9<10) = 3.
        Assert.Equal(3, model.TotalEstoqueBaixo);
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "INATIVO");
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "SEMMIN");
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "MINZERO");
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "MEIO");
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "SAUDOSO");
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "DEZ");

        // Zero estoque na frente.
        Assert.Equal("ZEBRA", model.EstoqueBaixo[0].Nome);
    }

    [Fact]
    public async Task Dashboard_EstoqueBaixo_LimitaAListaMasContaTodos()
    {
        using var qa = new QaContext();
        for (var i = 0; i < 8; i++)
        {
            qa.SeedProduto(nome: $"P{i:00}", quantidade: 0m, estoqueMinimo: 1m);
        }

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(5, model.EstoqueBaixo.Count);
        Assert.Equal(8, model.TotalEstoqueBaixo);
    }

    // =====================================================================
    // (12) ULTIMAS 5 VENDAS
    // =====================================================================

    [Fact]
    public async Task Dashboard_UltimasVendas_TrazAs5MaisRecentes()
    {
        using var qa = new QaContext();
        for (var i = 1; i <= 7; i++)
        {
            qa.SeedVenda(i * 10m, data: Hoje.AddHours(i));
        }
        // Uma venda antiga: nao entra nas 5 mais recentes.
        qa.SeedVenda(9999m, data: Ontem.AddHours(5));

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(5, model.UltimasVendas.Count);
        // 70, 60, 50, 40, 30 - as 5 maiores horas de hoje (1..7 viraram 10..70).
        Assert.Equal(70m, model.UltimasVendas[0].Total);
        Assert.Equal(60m, model.UltimasVendas[1].Total);
        Assert.Equal(50m, model.UltimasVendas[2].Total);
        Assert.Equal(40m, model.UltimasVendas[3].Total);
        Assert.Equal(30m, model.UltimasVendas[4].Total);
        Assert.DoesNotContain(model.UltimasVendas, v => v.Total == 9999m);
    }

    // =====================================================================
    // (13) BANCO VAZIO
    // =====================================================================

    [Fact]
    public async Task Dashboard_BancoVazio_TudoZeradoEListasVazias()
    {
        using var qa = new QaContext();

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(0m, model.TotalVendasHoje);
        Assert.Equal(0, model.NumeroVendasHoje);
        Assert.Equal(0m, model.TicketMedio);
        Assert.Equal(0m, model.SaldoGaveta);
        Assert.Equal(0, model.TotalEstoqueBaixo);
        Assert.Empty(model.TotalPorMetodo);
        Assert.Empty(model.UltimasVendas);
        // A serie de 7 dias continua com os 7 zeros: e o eixo do grafico.
        Assert.Equal(7, model.SerieUltimos7Dias.Count);
        Assert.All(model.SerieUltimos7Dias, d => Assert.Equal(0m, d.Total));
    }

    // =====================================================================
    // (14) SOMENTE LEITURA
    // =====================================================================

    [Fact]
    public async Task Dashboard_Index_NaoAlteraNenhumaContagem()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(9));
        qa.SeedVenda(200m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10), isFechado: true);
        qa.SeedVenda(300m, data: Ontem.AddHours(9));
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        qa.SeedProduto(nome: "BAIXO", quantidade: 0m, estoqueMinimo: 5m);

        var vendasAntes = qa.Context.Vendas.AsNoTracking().ToList();
        var movsAntes = qa.Context.CaixaMovimentacoes.AsNoTracking().ToList();
        var prodsAntes = qa.Context.Produtos.AsNoTracking().ToList();

        await ChamarIndex(qa.Context);

        var vendasDepois = qa.Context.Vendas.AsNoTracking().ToList();
        var movsDepois = qa.Context.CaixaMovimentacoes.AsNoTracking().ToList();
        var prodsDepois = qa.Context.Produtos.AsNoTracking().ToList();

        Assert.Equal(vendasAntes.Count, vendasDepois.Count);
        Assert.Equal(movsAntes.Count, movsDepois.Count);
        Assert.Equal(prodsAntes.Count, prodsDepois.Count);

        // Nenhum IsFechado pode ter virado true.
        Assert.All(vendasDepois, v => Assert.Equal(
            vendasAntes.Single(x => x.Id == v.Id).IsFechado, v.IsFechado));
        Assert.All(movsDepois, m => Assert.Equal(
            movsAntes.Single(x => x.Id == m.Id).IsFechado, m.IsFechado));
    }

    // =====================================================================
    // (15) AUTORIZACAO E SUPERFICIE HTTP (POR REFLEXAO)
    // =====================================================================

    [Fact]
    public async Task Dashboard_Controller_ExigePolicyDeFinanceiro()
    {
        var attr = typeof(DashboardController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        // Compara com a CONSTANTE, e nao com o literal: se o nome da policy
        // mudar no Security/Policies.cs, este teste quebra junto.
        Assert.Equal(Policies.PodeVerFinanceiro, attr!.Policy);
    }

    [Fact]
    public async Task Dashboard_Index_EUmGetReadOnly()
    {
        var index = typeof(DashboardController).GetMethod(nameof(DashboardController.Index))!;

        var httpGet = index.GetCustomAttributes()
            .Any(a => a.GetType().Name == "HttpGetAttribute");
        Assert.True(httpGet, "Index precisa ser [HttpGet].");

        // Nenhuma action que escreva: o painel e leitura pura.
        var verbs = typeof(DashboardController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes().Select(a => a.GetType().Name))
            .Where(verb => verb.StartsWith("Http") && verb != "HttpGetAttribute")
            .ToList();

        Assert.Empty(verbs);
    }
}