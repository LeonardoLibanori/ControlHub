using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// ETAPA D - Dashboard: clicar numa barra do grafico traz as vendas do dia
/// PARA BAIXO, no proprio Index (?data=), sem abrir a tela Dia.
///
/// SEPARADO do DashboardQaTests (ETAPA A) e do DashboardPeriodoQaTests
/// (ETAPA C) de proposito: aqueles fixam o painel de 7 dias e a janela
/// variavel; este arquivo cobre o dia escolhido pelo clique na barra.
///
/// O QUE ESTE ARQUIVO PROTEGE:
///   - data nula ou posterior a HOJE nao seleciona nada - nao ha venda no
///     futuro (mesma regra da tela Dia); a comparacao com Today vem ANTES
///     do AddDays, entao o passo nunca passa de DateTime.MaxValue.
///   - o dia selecionado devolve a lista em ordem cronologica decrescente
///     e o TOTAL do TotalVenda GRAVADO, somado em decimal EM C#.
///   - o dia selecionado nao muda com a janela (7 ou 30): ?data= manda.
///   - fronteiras do dia sao [00:00, 24:00) locais (regra 23).
///   - leitura pura: nada no banco muda (regra 25).
/// </summary>
public class DashboardVendasDoDiaQaTests
{
    private static DateTime Hoje => DateTime.Today;
    private static DateTime Ontem => DateTime.Today.AddDays(-1);
    private static DateTime Amanha => DateTime.Today.AddDays(1);

    private static async Task<DashboardViewModel> ChamarIndex(ApplicationDbContext ctx, DateTime? data = null)
    {
        var controller = new DashboardController(ctx);
        var result = await controller.Index(null, data);
        var view = Assert.IsAssignableFrom<ViewResult>(result);
        return Assert.IsType<DashboardViewModel>(view.Model);
    }

    // =====================================================================
    // (1) SEM ?data=: nada selecionado (o painel abre como antes)
    // =====================================================================

    [Fact]
    public async Task DashboardSemData_NenhumDiaSelecionado()
    {
        using var qa = new QaContext();
        qa.SeedVenda(50m, data: Ontem.AddHours(10));

        var model = await ChamarIndex(qa.Context);

        Assert.Null(model.DiaSelecionado);
        Assert.Empty(model.VendasDoDia);
        Assert.Equal(0m, model.TotalDiaSelecionado);
        Assert.Equal(0, model.NumeroVendasDia);
        // O campo antigo continua alimentado - os testes ETAPA A protegem ele.
        Assert.Single(model.UltimasVendas);
    }

    // =====================================================================
    // (2) DATA FUTURA: nao ha venda no futuro, entao nada e selecionado
    // =====================================================================

    [Fact]
    public async Task DashboardDataFutura_NenhumDiaSelecionado()
    {
        using var qa = new QaContext();

        var model = await ChamarIndex(qa.Context, Amanha);

        Assert.Null(model.DiaSelecionado);
        Assert.Empty(model.VendasDoDia);
        Assert.Equal(0m, model.TotalDiaSelecionado);
    }

    // =====================================================================
    // (3) DIA COM VENDAS: lista, total GRAVADO e contagem
    // =====================================================================

    [Fact]
    public async Task DashboardDiaComVendas_ListaTotalEContagem()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Ontem.AddHours(10));
        qa.SeedVenda(200m, data: Ontem.AddHours(11));
        qa.SeedVenda(999m, data: Hoje.AddHours(10));                     // FORA: outro dia
        qa.SeedVenda(888m, data: Ontem.AddDays(-1).AddHours(10));        // FORA: outro dia

        // ?data= chega como "ontem 15h" de proposito: o painel normaliza
        // para a MEIA-NOITE do dia (o href da barra manda yyyy-MM-dd).
        var model = await ChamarIndex(qa.Context, Ontem.AddHours(15));

        Assert.Equal(Ontem, model.DiaSelecionado);
        Assert.Equal(2, model.VendasDoDia.Count);
        Assert.Equal(300m, model.TotalDiaSelecionado);
        Assert.Equal(2, model.NumeroVendasDia);
    }

    // =====================================================================
    // (4) ORDEM: mais recente primeiro (igual a tela Dia)
    // =====================================================================

    [Fact]
    public async Task DashboardDiaComVendas_MaisRecentePrimeiro()
    {
        using var qa = new QaContext();
        var cedo = qa.SeedVenda(10m, data: Ontem.AddHours(9));
        var meio = qa.SeedVenda(20m, data: Ontem.AddHours(15));
        var tarde = qa.SeedVenda(30m, data: Ontem.AddHours(18));

        var model = await ChamarIndex(qa.Context, Ontem);

        Assert.Equal(new[] { tarde.Id, meio.Id, cedo.Id },
            model.VendasDoDia.Select(v => v.Id).ToArray());
    }

    // =====================================================================
    // (5) DIA SEM VENDAS: seleciona mesmo assim (a view mostra o vazio)
    // =====================================================================

    [Fact]
    public async Task DashboardDiaSemVendas_DiaSelecionadoListaVazia()
    {
        using var qa = new QaContext();

        var model = await ChamarIndex(qa.Context, Hoje.AddDays(-5));

        Assert.Equal(Hoje.AddDays(-5), model.DiaSelecionado);
        Assert.Empty(model.VendasDoDia);
        Assert.Equal(0m, model.TotalDiaSelecionado);
        Assert.Equal(0, model.NumeroVendasDia);
    }

    // =====================================================================
    // (6) FRONTEIRAS DO DIA: [00:00, 24:00) locais (regra 23)
    // =====================================================================

    [Fact]
    public async Task DashboardDia_FronteirasMeiaNoiteEUltimoTick()
    {
        using var qa = new QaContext();
        qa.SeedVenda(10m, data: Ontem);                                  // 00:00:00: conta
        qa.SeedVenda(20m, data: Ontem.AddDays(1).AddTicks(-1));          // 23:59:59.9999999: conta
        qa.SeedVenda(30m, data: Hoje);                                   // 00:00 de hoje: FORA
        qa.SeedVenda(40m, data: Ontem.AddDays(-1).AddTicks(-1));         // ultimo tick de anteontem: FORA

        var model = await ChamarIndex(qa.Context, Ontem);

        Assert.Equal(2, model.VendasDoDia.Count);
        Assert.Equal(30m, model.TotalDiaSelecionado);
    }

    // =====================================================================
    // (7) ?dias= e ?data= andam juntos: a janela nao muda o dia escolhido
    // =====================================================================

    [Fact]
    public async Task DashboardDias30ComData_ODiaSelecionadoNaoMudaComAJanela()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Ontem.AddHours(10));
        qa.SeedVenda(50m, data: Ontem.AddDays(-10).AddHours(10)); // dentro de 30, fora de 7

        var controller = new DashboardController(qa.Context);
        var result = await controller.Index(30, Ontem);
        var view = Assert.IsAssignableFrom<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);

        Assert.Equal(30, model.Dias);
        Assert.Equal(Ontem, model.DiaSelecionado);
        Assert.Single(model.VendasDoDia);
    }

    // =====================================================================
    // (8) SOMENTE LEITURA: o clique nao mexe em venda, movimento ou produto
    // =====================================================================

    [Fact]
    public async Task DashboardDiaSelecionado_NaoAlteraNadaNoBanco()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Ontem.AddHours(9));
        qa.SeedVenda(200m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(10), isFechado: true);
        qa.SeedMovimentacao("Suprimento", 50m, data: Ontem.AddHours(8));
        qa.SeedProduto(nome: "BAIXO", quantidade: 0m, estoqueMinimo: 5m);

        var antes = qa.Context.Vendas.AsNoTracking()
            .OrderBy(v => v.Id)
            .Select(v => new { v.Id, v.IsFechado, v.TotalVenda })
            .ToList();
        var movsAntes = qa.Context.CaixaMovimentacoes.AsNoTracking().Count();
        var prodsAntes = qa.Context.Produtos.AsNoTracking().Count();

        await ChamarIndex(qa.Context, Ontem);

        var depois = qa.Context.Vendas.AsNoTracking()
            .OrderBy(v => v.Id)
            .Select(v => new { v.Id, v.IsFechado, v.TotalVenda })
            .ToList();

        Assert.Equal(antes, depois);
        Assert.Equal(movsAntes, qa.Context.CaixaMovimentacoes.AsNoTracking().Count());
        Assert.Equal(prodsAntes, qa.Context.Produtos.AsNoTracking().Count());
    }
}
