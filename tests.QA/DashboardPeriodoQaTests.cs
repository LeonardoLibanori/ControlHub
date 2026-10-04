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
/// ETAPA C - Dashboard com 7/30 dias e detalhe por dia.
///
/// SEPARADO do DashboardQaTests de proposito: aquele fixa o comportamento de
/// 7 dias que ja existe (e seus testes nao podem ser alterados). Este arquivo
/// cobre a JANELA VARIÁVEL e a tela de detalhe.
///
/// O QUE ESTE ARQUIVO PROTEGE:
///   - "hoje" continua sendo INTERVALO local (regra 23), agora numa janela de
///     N dias: [Today-(N-1), Today+1). N 30 casas 30 AddDays, nunca
///     DateTime.MaxValue.
///   - Todo dinheiro é TotalVenda GRAVADO, somado em decimal EM C#.
///   - Quantidade NUNCA é somada em SQL: vive em TEXT no SQLite e a
///     collation BINARY ordenaria "10" antes de "9" (regra 2).
/// </summary>
public class DashboardPeriodoQaTests
{
    private static DateTime Hoje => DateTime.Today;
    private static DateTime Ontem => DateTime.Today.AddDays(-1);

    private static async Task<DashboardViewModel> ChamarIndex(ApplicationDbContext ctx, int? dias = null)
    {
        var controller = new DashboardController(ctx);
        var result = await controller.Index(dias);
        var view = Assert.IsAssignableFrom<ViewResult>(result);
        return Assert.IsType<DashboardViewModel>(view.Model);
    }

    // =====================================================================
    // (a) ?dias= aceita SO 7 e 30; qualquer outra coisa vira 7
    // =====================================================================

    [Theory]
    [InlineData(null, 7)]      // padrao
    [InlineData(7, 7)]
    [InlineData(30, 30)]
    [InlineData(0, 7)]
    [InlineData(-1, 7)]
    [InlineData(1, 7)]
    [InlineData(15, 7)]
    [InlineData(31, 7)]
    [InlineData(999, 7)]
    [InlineData(999999999, 7)]
    public async Task DashboardPeriodo_SoAceita7Ou30_QualquerOutroVira7(int? entrada, int esperado)
    {
        using var qa = new QaContext();

        var model = await ChamarIndex(qa.Context, entrada);

        Assert.Equal(esperado, model.Dias);
        Assert.Equal(esperado, model.SerieUltimos7Dias.Count);
    }

    // =====================================================================
    // (b) A JANELA: N pontos, cronologica, com zeros, sem vazar
    // =====================================================================

    [Fact]
    public async Task DashboardPeriodo_TrintaDias_Tem30PontosEmOrdemCronologica()
    {
        using var qa = new QaContext();
        qa.SeedVenda(11m, data: Hoje.AddHours(10));                    // hoje
        qa.SeedVenda(22m, data: Hoje.AddDays(-1).AddHours(10));      // -1
        qa.SeedVenda(44m, data: Hoje.AddDays(-29).AddHours(10));     // -29 (primeiro da janela)
        qa.SeedVenda(99m, data: Hoje.AddDays(-30).AddHours(10));     // -30: FORA
        qa.SeedVenda(88m, data: Hoje.AddDays(-31).AddHours(10));     // fora tambem

        var model = await ChamarIndex(qa.Context, 30);

        Assert.Equal(30, model.SerieUltimos7Dias.Count);

        // Cronologica: de -29 ate hoje.
        for (var i = 0; i < model.SerieUltimos7Dias.Count; i++)
        {
            Assert.Equal(Hoje.AddDays(-29 + i), model.SerieUltimos7Dias[i].Dia);
        }

        Assert.Equal(44m, model.SerieUltimos7Dias[0].Total);  // -29
        Assert.Equal(22m, model.SerieUltimos7Dias[28].Total); // -1
        Assert.Equal(11m, model.SerieUltimos7Dias[29].Total); // hoje

        // Nada de fora da janela.
        Assert.DoesNotContain(model.SerieUltimos7Dias, d => d.Total == 99m);
        Assert.DoesNotContain(model.SerieUltimos7Dias, d => d.Total == 88m);

        // Dia sem venda = 0, e a soma bate com a consulta direta.
        Assert.Contains(model.SerieUltimos7Dias, d => d.Total == 0m);
        var esperado = qa.Context.Vendas
            .Where(v => v.DataVenda >= Hoje.AddDays(-29) && v.DataVenda < Hoje.AddDays(1))
            .Sum(v => v.TotalVenda);
        Assert.Equal(esperado, model.SerieUltimos7Dias.Sum(d => d.Total));
    }

    [Fact]
    public async Task DashboardPeriodo_SeteDias_ContinuaCom7EContinua7Pontos()
    {
        using var qa = new QaContext();
        qa.SeedVenda(11m, data: Hoje.AddHours(10));
        qa.SeedVenda(99m, data: Hoje.AddDays(-7).AddHours(10)); // fora da janela de 7

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(7, model.SerieUltimos7Dias.Count);
        Assert.Equal(7, model.Dias);
        for (var i = 0; i < model.SerieUltimos7Dias.Count; i++)
        {
            Assert.Equal(Hoje.AddDays(-6 + i), model.SerieUltimos7Dias[i].Dia);
        }
        Assert.Equal(11m, model.SerieUltimos7Dias[6].Total);
        Assert.DoesNotContain(model.SerieUltimos7Dias, d => d.Total == 99m);
    }

    // =====================================================================
    // (c) Total e nº de vendas DO PERIODO
    // =====================================================================

    [Fact]
    public async Task DashboardPeriodo_TotalEQuantidadeDoPeriodo()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(9));
        qa.SeedVenda(200m, data: Hoje.AddDays(-1).AddHours(9));
        qa.SeedVenda(300m, data: Hoje.AddDays(-2).AddHours(9));
        qa.SeedVenda(999m, data: Hoje.AddDays(-10).AddHours(9)); // fora de 7

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(600m, model.TotalVendasPeriodo);
        Assert.Equal(3, model.NumeroVendasPeriodo);

        var model30 = await ChamarIndex(qa.Context, 30);
        Assert.Equal(999m + 600m, model30.TotalVendasPeriodo);
        Assert.Equal(4, model30.NumeroVendasPeriodo);
    }

    // =====================================================================
    // (d) ALTURA DA BARRA (calculada no view-model)
    // =====================================================================

    [Fact]
    public async Task DashboardPeriodo_Altura_ODiaMaiorEH100EOZeroEHZero()
    {
        using var qa = new QaContext();
        qa.SeedVenda(1000m, data: Hoje.AddHours(9)); // o maior
        qa.SeedVenda(250m, data: Hoje.AddDays(-1).AddHours(9));

        var model = await ChamarIndex(qa.Context);

        Assert.Equal(100m, model.SerieUltimos7Dias[6].PercentualAltura);
        Assert.Equal(25m, model.SerieUltimos7Dias[5].PercentualAltura);
        Assert.Equal(0m, model.SerieUltimos7Dias[0].PercentualAltura);
    }

    [Fact]
    public async Task DashboardPeriodo_Altura_ValorPequenoMasPositivoNuncaSome()
    {
        using var qa = new QaContext();
        // 9,98 e ~1% do maximo de 1.000: sem piso, a barra arredondaria
        // para 0px e o dia SUMIRIA do grafico.
        qa.SeedVenda(1000m, data: Hoje.AddHours(9));
        qa.SeedVenda(9.98m, data: Hoje.AddDays(-1).AddHours(9));

        var model = await ChamarIndex(qa.Context);

        var pequeno = model.SerieUltimos7Dias[5];
        Assert.Equal(9.98m, pequeno.Total);
        Assert.True(pequeno.Total > 0m, "o dia tem venda");
        Assert.True(pequeno.PercentualAltura >= 4m,
            $"um dia com venda nao pode ficar com barra de 0%: veio {pequeno.PercentualAltura}");
    }

    [Fact]
    public async Task DashboardPeriodo_Altura_TudoZeroNaoDividePorZeroNemEstoura()
    {
        using var qa = new QaContext();

        var model = await ChamarIndex(qa.Context, 30);

        Assert.Equal(30, model.SerieUltimos7Dias.Count);
        Assert.All(model.SerieUltimos7Dias, d => Assert.Equal(0m, d.PercentualAltura));
        Assert.Equal(0m, model.TotalVendasPeriodo);
    }

    // =====================================================================
    // (e/f) DIA: intervalo, total gravado e paridade com o Historico
    // =====================================================================

    private static async Task<DashboardDiaViewModel> ChamarDia(ApplicationDbContext ctx, DateTime? data)
    {
        var controller = new DashboardController(ctx);
        var result = await controller.Dia(data);
        var view = Assert.IsAssignableFrom<ViewResult>(result);
        return Assert.IsType<DashboardDiaViewModel>(view.Model);
    }

    [Fact]
    public async Task DashboardDia_TrazSomenteVendasDoDia()
    {
        using var qa = new QaContext();
        // Atencao: o dia ALVO e Ontem, entao tudo em Ontem conta.
        qa.SeedVenda(100m, data: Ontem.AddHours(10));               // 10h do alvo: conta
        qa.SeedVenda(10m, data: Ontem);                             // 00:00:00: conta
        qa.SeedVenda(20m, data: Ontem.AddDays(1).AddTicks(-1));     // 23:59:59.9999999: conta
        qa.SeedVenda(500m, data: Hoje);                             // 00:00 de hoje: FORA
        qa.SeedVenda(300m, data: Ontem.AddHours(15), isFechado: true); // fechada CONTA

        var model = await ChamarDia(qa.Context, Ontem);

        // 100 + 10 + 20 + 300 = 430
        Assert.Equal(430m, model.Total);
        Assert.Equal(4, model.NumeroVendas);
        Assert.Equal(107.5m, model.TicketMedio);
        Assert.Equal(430m, model.Vendas.Sum(v => v.Total));
        Assert.DoesNotContain(model.Vendas, v => v.Total == 500m);
    }

    [Fact]
    public async Task DashboardDia_TotalIgualAoHistoricoDoMesmoDia()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(9));
        qa.SeedVenda(250m, metodoPagamento: "Credito", data: Ontem.AddHours(11));
        qa.SeedVenda(333m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));

        var model = await ChamarDia(qa.Context, Ontem);

        var controller = new CaixaController(qa.Context);
        var resultado = await controller.Historico(Ontem);
        var listada = ((ViewResult)resultado).Model as IEnumerable<Venda>;

        Assert.NotNull(listada);
        Assert.Equal(listada!.Sum(v => v.TotalVenda), model.Total);
    }

    // =====================================================================
    // (g) PRODUTOS: soma de Quantidade EM C#
    // =====================================================================

    [Fact]
    public async Task DashboardDia_Produtos_SomaQuantidadeEmCentavosDeReais()
    {
        using var qa = new QaContext();
        // 0,125 + 0,375 = 0,5 kg: em float isso dari 0,49999999.
        var queijo = qa.SeedProduto(nome: "QUEIJO", quantidade: 10m,
            preco: 10m, tipo: TipoProduto.PorPeso);
        var cafe = qa.SeedProduto(nome: "CAFE", quantidade: 10m,
            preco: 10m, tipo: TipoProduto.Prateleira);

        Venda ComItem(int produtoId, decimal qtd, decimal preco)
        {
            // ConversorCentavos RECUSA dinheiro com mais de 2 casas, entao o
            // total gravado precisa ser um valor de caixa legitimo (0,125 kg a
            // R$ 39,90 daria R$ 4,9875 e o SaveChanges explodiria). O item
            // guarda a fracao exata; so o TOTAL VENDA vai para dinheiro.
            var totalVenda = Math.Round(qtd * preco, 2);
            var v = qa.SeedVenda(totalVenda, data: Ontem.AddHours(9));
            v.Itens.Add(new VendaItem
            {
                ProdutoId = produtoId,
                Quantidade = qtd,
                PrecoUnitario = preco,
            });
            return v;
        }

        ComItem(queijo.Id, 0.125m, 39.90m);
        ComItem(queijo.Id, 0.375m, 39.90m);
        ComItem(cafe.Id, 9m, 4m);
        ComItem(cafe.Id, 10m, 4m);
        qa.Context.SaveChanges();

        var model = await ChamarDia(qa.Context, Ontem);

        var q = Assert.Single(model.Produtos, p => p.Nome == "QUEIJO");
        Assert.Equal(0.5m, q.Quantidade);
        Assert.Equal("kg", q.Unidade);
        Assert.Equal(19.95m, q.Valor); // 0,5 x 39,90

        var c = Assert.Single(model.Produtos, p => p.Nome == "CAFE");
        Assert.Equal(19m, c.Quantidade);  // 9 + 10 NUMERICAMENTE
        Assert.Equal("un", c.Unidade);
        Assert.Equal(76m, c.Valor);
    }

    [Fact]
    public async Task DashboardDia_Produtos_OrdenaPorValorEIncluiInativo()
    {
        using var qa = new QaContext();
        var caro = qa.SeedProduto(nome: "CARO", quantidade: 5m, preco: 100m);
        var barato = qa.SeedProduto(nome: "BARATO", quantidade: 5m, preco: 1m);
        var inativo = qa.SeedProduto(nome: "INATIVO", quantidade: 5m, preco: 50m);

        foreach (var (id, preco) in new[] { (caro.Id, 100m), (barato.Id, 1m), (inativo.Id, 50m) })
        {
            var v = qa.SeedVenda(1m, data: Ontem.AddHours(9));
            v.Itens.Add(new VendaItem { ProdutoId = id, Quantidade = 1m, PrecoUnitario = preco });
        }
        qa.Context.SaveChanges();

        inativo.Ativo = false;
        qa.Context.SaveChanges();

        var model = await ChamarDia(qa.Context, Ontem);

        // Produto desativado HOJE aparece: ele vendeu no dia.
        Assert.Contains(model.Produtos, p => p.Nome == "INATIVO");
        // Ordenado por valor desc: 100, 50, 1
        Assert.Equal(new[] { "CARO", "INATIVO", "BARATO" },
            model.Produtos.Select(p => p.Nome).ToArray());
    }

    // =====================================================================
    // (h) Data invalida / futura -> redirect; dia vazio nao estoura
    // =====================================================================

    [Fact]
    public async Task DashboardDia_SemDataRedirecionaParaOPainel()
    {
        using var qa = new QaContext();
        var controller = new DashboardController(qa.Context);

        Assert.IsType<RedirectToActionResult>(await controller.Dia(null));
    }

    [Fact]
    public async Task DashboardDia_DataFuturaRedirecionaParaOPainel()
    {
        using var qa = new QaContext();
        var controller = new DashboardController(qa.Context);

        // Amanha: recusado. E o dia de hoje (meia-noite) TAMBEM e futuro
        // no instante da chamada, entao usamos amanha explicitamente.
        var result = await controller.Dia(Hoje.AddDays(1));

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task DashboardDia_SemVendas_ModeloVazioSemErro()
    {
        using var qa = new QaContext();

        var model = await ChamarDia(qa.Context, Hoje.AddDays(-5));

        Assert.Equal(0m, model.Total);
        Assert.Equal(0, model.NumeroVendas);
        Assert.Equal(0m, model.TicketMedio);
        Assert.Empty(model.Produtos);
        Assert.Empty(model.Vendas);
        Assert.Equal(Hoje.AddDays(-5), model.Dia);
    }

    // =====================================================================
    // (i) SOMENTE LEITURA
    // =====================================================================

    [Fact]
    public async Task DashboardDia_NaoAlteraContagensNemIsFechado()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Ontem.AddHours(9));
        qa.SeedVenda(200m, data: Ontem.AddHours(10), isFechado: true);
        qa.SeedProduto(nome: "P1", quantidade: 1m, preco: 5m);

        var vendasAntes = qa.Context.Vendas.AsNoTracking().ToList();
        var prodsAntes = qa.Context.Produtos.AsNoTracking().ToList();

        await ChamarDia(qa.Context, Ontem);
        await ChamarIndex(qa.Context, 30);

        var vendasDepois = qa.Context.Vendas.AsNoTracking().ToList();
        var prodsDepois = qa.Context.Produtos.AsNoTracking().ToList();

        Assert.Equal(vendasAntes.Count, vendasDepois.Count);
        Assert.Equal(prodsAntes.Count, prodsDepois.Count);
        Assert.All(vendasDepois, v => Assert.Equal(
            vendasAntes.Single(x => x.Id == v.Id).IsFechado, v.IsFechado));
    }

    // =====================================================================
    // (j) AUTORIZACAO E SUPERFICIE HTTP
    // =====================================================================

    [Fact]
    public void Dashboard_DiaEUmGetEClasseExigeFinanceiro()
    {
        var dia = typeof(DashboardController).GetMethod(nameof(DashboardController.Dia))!;

        Assert.True(dia.GetCustomAttributes().Any(a => a.GetType().Name == "HttpGetAttribute"),
            "Dia precisa ser [HttpGet].");

        var attr = typeof(DashboardController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(Policies.PodeVerFinanceiro, attr!.Policy);

        // Nenhuma action que escreva.
        var verbos = typeof(DashboardController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes().Select(a => a.GetType().Name))
            .Where(v => v.StartsWith("Http") && v != "HttpGetAttribute")
            .ToList();

        Assert.Empty(verbos);
    }
}