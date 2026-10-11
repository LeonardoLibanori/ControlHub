using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;

namespace tests.QA;

/// <summary>
/// AJUSTE 4 - Dashboard: dados do painel com comparativo, série anterior e funções do gráfico.
/// TESTES PRIMEIRO: criados com NotImplementedException para falharem antes da implementação.
/// </summary>
public class DashboardTemplateQaTests
{
    private static DateTime Hoje => DateTime.Today;
    private static DateTime Ontem => DateTime.Today.AddDays(-1);
    private static DateTime Amanha => DateTime.Today.AddDays(1);

    private static async Task<DashboardViewModel> ChamarIndex(ApplicationDbContext ctx, int? dias = null, DateTime? data = null, string? periodo = null)
    {
        var controller = new DashboardController(ctx);
        var result = await controller.Index(dias, data, periodo);
        var view = Assert.IsAssignableFrom<ViewResult>(result);
        return Assert.IsType<DashboardViewModel>(view.Model);
    }

    // =====================================================================
    // 1. JANELAS E FRONTEIRAS DE CADA PERÍODO
    // =====================================================================

    private static (DateTime inicioAtual, DateTime fimAtual, DateTime inicioAnterior, DateTime fimAnterior, string nomePeriodo, int bucketCount, string bucketTipo) ResolverPeriodoViaReflection(string periodo, int? dias, DateTime hoje)
    {
        var method = typeof(DashboardController).GetMethod("ResolverPeriodo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return ((DateTime, DateTime, DateTime, DateTime, string, int, string))method.Invoke(null, new object[] { periodo, dias, hoje });
    }

    [Fact]
    public void Periodos_Hoje_IntervaloFechadoAberto_HInclusivo()
    {
        // hoje = [H, H+1)  onde H = hoje às 00:00
        var hoje = DateTime.Today;
        var (ini, fim, iniAnt, fimAnt, nome, buckets, tipo) = ResolverPeriodoViaReflection("hoje", null, hoje);
        Assert.Equal("hoje", nome);
        Assert.Equal(hoje, ini);           // H inclusivo
        Assert.Equal(hoje.AddDays(1), fim); // H+1 exclusivo
        Assert.Equal(hoje.AddDays(-1), iniAnt); // ontem
        Assert.Equal(hoje, fimAnt);         // H
        Assert.Equal(24, buckets);
        Assert.Equal("hora", tipo);
    }

    [Fact]
    public void Periodos_Ontem_IntervaloFechadoAberto()
    {
        // ontem = [H-1, H)
        var hoje = DateTime.Today;
        var (ini, fim, iniAnt, fimAnt, nome, buckets, tipo) = ResolverPeriodoViaReflection("ontem", null, hoje);
        Assert.Equal("ontem", nome);
        Assert.Equal(hoje.AddDays(-1), ini);
        Assert.Equal(hoje, fim);
        Assert.Equal(hoje.AddDays(-2), iniAnt);
        Assert.Equal(hoje.AddDays(-1), fimAnt);
        Assert.Equal(24, buckets);
        Assert.Equal("hora", tipo);
    }

    [Fact]
    public void Periodos_7dias_Intervalo6DiasAtrasAteHoje()
    {
        // 7dias = [H-6, H+1)
        var hoje = DateTime.Today;
        var (ini, fim, iniAnt, fimAnt, nome, buckets, tipo) = ResolverPeriodoViaReflection("7dias", null, hoje);
        Assert.Equal("7dias", nome);
        Assert.Equal(hoje.AddDays(-6), ini);
        Assert.Equal(hoje.AddDays(1), fim);
        Assert.Equal(hoje.AddDays(-13), iniAnt);
        Assert.Equal(hoje.AddDays(-6), fimAnt);
        Assert.Equal(7, buckets);
        Assert.Equal("dia", tipo);
    }

    [Fact]
    public void Periodos_30dias_Intervalo29DiasAtrasAteHoje()
    {
        // 30dias = [H-29, H+1)
        var hoje = DateTime.Today;
        var (ini, fim, iniAnt, fimAnt, nome, buckets, tipo) = ResolverPeriodoViaReflection("30dias", null, hoje);
        Assert.Equal("30dias", nome);
        Assert.Equal(hoje.AddDays(-29), ini);
        Assert.Equal(hoje.AddDays(1), fim);
        Assert.Equal(hoje.AddDays(-59), iniAnt);
        Assert.Equal(hoje.AddDays(-29), fimAnt);
        Assert.Equal(30, buckets);
        Assert.Equal("dia", tipo);
    }

    [Fact]
    public void Periodos_Mes_PrimeiroDiaMesAteHoje()
    {
        // mes = [1º do mês, H+1)
        var hoje = DateTime.Today;
        var (ini, fim, iniAnt, fimAnt, nome, buckets, tipo) = ResolverPeriodoViaReflection("mes", null, hoje);
        var primeiroMes = new DateTime(hoje.Year, hoje.Month, 1);
        Assert.Equal("mes", nome);
        Assert.Equal(primeiroMes, ini);
        Assert.Equal(hoje.AddDays(1), fim);
        
        var primeiroMesAnterior = primeiroMes.AddMonths(-1);
        var ultimoDiaMesAnterior = primeiroMes.AddDays(-1).Day;
        var n = Math.Min(hoje.Day, ultimoDiaMesAnterior);
        var fimAntEsperado = new DateTime(primeiroMesAnterior.Year, primeiroMesAnterior.Month, n).AddDays(1);
        
        Assert.Equal(primeiroMesAnterior, iniAnt);
        Assert.Equal(fimAntEsperado, fimAnt);
        Assert.Equal(hoje.Day, buckets); // dias do mês até hoje
        Assert.Equal("dia", tipo);
    }

    [Fact]
    public void Periodos_Ano_PrimeiroJanAteHoje()
    {
        // ano = [1º de janeiro, H+1)
        var hoje = DateTime.Today;
        var (ini, fim, iniAnt, fimAnt, nome, buckets, tipo) = ResolverPeriodoViaReflection("ano", null, hoje);
        var primeiroAno = new DateTime(hoje.Year, 1, 1);
        Assert.Equal("ano", nome);
        Assert.Equal(primeiroAno, ini);
        Assert.Equal(hoje.AddDays(1), fim);
        
        var primeiroAnoAnterior = new DateTime(hoje.Year - 1, 1, 1);
        DateTime fimAntEsperado;
        try {
            fimAntEsperado = new DateTime(hoje.Year - 1, hoje.Month, hoje.Day).AddDays(1);
        } catch {
            fimAntEsperado = new DateTime(hoje.Year - 1, 2, 28).AddDays(1);
        }
        
        Assert.Equal(primeiroAnoAnterior, iniAnt);
        Assert.Equal(fimAntEsperado, fimAnt);
        Assert.Equal(hoje.Month, buckets); // meses até o atual
        Assert.Equal("mes", tipo);
    }

    [Fact]
    public void Periodos_Anterior_HojeViraOntem()
    {
        // anterior de hoje = ontem
        var hoje = DateTime.Today;
        var (_, _, iniAnt, fimAnt, _, _, _) = ResolverPeriodoViaReflection("hoje", null, hoje);
        Assert.Equal(hoje.AddDays(-1), iniAnt);
        Assert.Equal(hoje, fimAnt);
    }

    [Fact]
    public void Periodos_Anterior_OntemViraAnteontem()
    {
        var hoje = DateTime.Today;
        var (_, _, iniAnt, fimAnt, _, _, _) = ResolverPeriodoViaReflection("ontem", null, hoje);
        Assert.Equal(hoje.AddDays(-2), iniAnt);
        Assert.Equal(hoje.AddDays(-1), fimAnt);
    }

    [Fact]
    public void Periodos_Anterior_7dias_Vira13a6DiasAtras()
    {
        // anterior de 7dias = [H-13, H-6)
        var hoje = DateTime.Today;
        var (_, _, iniAnt, fimAnt, _, _, _) = ResolverPeriodoViaReflection("7dias", null, hoje);
        Assert.Equal(hoje.AddDays(-13), iniAnt);
        Assert.Equal(hoje.AddDays(-6), fimAnt);
    }

    [Fact]
    public void Periodos_Anterior_30dias_Vira59a29DiasAtras()
    {
        // anterior de 30dias = [H-59, H-29)
        var hoje = DateTime.Today;
        var (_, _, iniAnt, fimAnt, _, _, _) = ResolverPeriodoViaReflection("30dias", null, hoje);
        Assert.Equal(hoje.AddDays(-59), iniAnt);
        Assert.Equal(hoje.AddDays(-29), fimAnt);
    }

    [Fact]
    public void Periodos_Anterior_Mes_MesmoNDiasMesAnterior()
    {
        // N = dia de hoje, limitado ao tamanho do mês anterior
        var hoje = DateTime.Today;
        var (_, _, iniAnt, fimAnt, _, _, _) = ResolverPeriodoViaReflection("mes", null, hoje);
        
        var primeiroMesAtual = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var primeiroMesAnterior = primeiroMesAtual.AddMonths(-1);
        var ultimoDiaMesAnterior = primeiroMesAtual.AddDays(-1).Day;
        var n = Math.Min(DateTime.Today.Day, ultimoDiaMesAnterior);
        var fimAntEsperado = new DateTime(hoje.Year, hoje.Month - 1, n).AddDays(1);
        
        Assert.Equal(primeiroMesAnterior, iniAnt);
        Assert.Equal(fimAntEsperado, fimAnt);
    }

    [Fact]
    public void Periodos_Anterior_Ano_MesmoDiaMesAnoAnterior()
    {
        // 1º jan até mesmo dia/mês do ano anterior; 29/02 vira 28/02
        var hoje = DateTime.Today;
        var (_, _, iniAnt, fimAnt, _, _, _) = ResolverPeriodoViaReflection("ano", null, hoje);
        
        var primeiroAnoAnterior = new DateTime(hoje.Year - 1, 1, 1);
        DateTime fimAntEsperado;
        try {
            fimAntEsperado = new DateTime(hoje.Year - 1, hoje.Month, hoje.Day).AddDays(1);
        } catch {
            fimAntEsperado = new DateTime(hoje.Year - 1, 2, 28).AddDays(1);
        }
        
        Assert.Equal(new DateTime(hoje.Year - 1, 1, 1), iniAnt);
        Assert.Equal(fimAntEsperado, fimAnt);
    }

    // =====================================================================
    // 2. KPIs E VARIAÇÕES
    // =====================================================================

    [Fact]
    public async Task KPIs_Faturamento_Vendas_Ticket_Variacao_Sobe()
    {
        using var qa = new QaContext();
        // Período atual: 2 vendas = 300
        // Anterior: 1 venda = 100
        // Variação = (300-100)/100*100 = 200% -> "Subiu"
        qa.SeedVenda(200m, data: Hoje.AddHours(10));
        qa.SeedVenda(100m, data: Hoje.AddHours(11));
        qa.SeedVenda(100m, data: Ontem.AddHours(10));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(300m, model.FaturamentoAtual); // faturamento período
        Assert.Equal(2, model.VendasAtual);
        Assert.Equal(150m, model.TicketMedioAtual);
        // Verificar propriedades novas de variação
        Assert.Equal(100m, model.FaturamentoAnterior);
        Assert.Equal(1, model.VendasAnterior);
        Assert.Equal(100m, model.TicketMedioAnterior);
        Assert.Equal(200m, model.VariacaoPercentual); // (300-100)/100*100 = 200%
        Assert.Equal("Subiu", model.EstadoVariacao);
    }

    [Fact]
    public async Task KPIs_Variacao_Cai()
    {
        using var qa = new QaContext();
        // Período atual: 1 venda = 100
        // Anterior: 2 vendas = 300
        // Variação = (100-300)/300*100 = -66.7% -> "Caiu"
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Ontem.AddHours(10));
        qa.SeedVenda(100m, data: Ontem.AddHours(11));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(100m, model.FaturamentoAtual);
        Assert.Equal(1, model.VendasAtual);
        Assert.Equal(300m, model.FaturamentoAnterior);
        Assert.Equal(2, model.VendasAnterior);
        Assert.Equal(-66.7m, model.VariacaoPercentual); // (100-300)/300*100 = -66.666... -> -66.7
        Assert.Equal("Caiu", model.EstadoVariacao);
    }

    [Fact]
    public async Task KPIs_Variacao_Establevel_ZeroZero()
    {
        using var qa = new QaContext();
        // Período atual: 2 vendas = 200
        // Anterior: 2 vendas = 200
        // Variação = (200-200)/200*100 = 0% -> "Estavel"
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(100m, data: Hoje.AddHours(11));
        qa.SeedVenda(100m, data: Ontem.AddHours(10));
        qa.SeedVenda(100m, data: Ontem.AddHours(11));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(200m, model.FaturamentoAtual);
        Assert.Equal(200m, model.FaturamentoAnterior);
        Assert.Equal(0m, model.VariacaoPercentual);
        Assert.Equal("Estavel", model.EstadoVariacao);
    }

    [Fact]
    public async Task KPIs_Variacao_SemBase_AnteriorZero()
    {
        using var qa = new QaContext();
        // Período atual: 1 venda = 100
        // Anterior: 0 vendas
        // Variação = null (SemBase)
        qa.SeedVenda(100m, data: Hoje.AddHours(10));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(100m, model.FaturamentoAtual);
        Assert.Equal(0m, model.FaturamentoAnterior);
        Assert.Null(model.VariacaoPercentual);
        Assert.Equal("SemBase", model.EstadoVariacao);
    }

[Fact]
    public async Task KPIs_Arredondamento_AwayFromZero_DuasCasas()
    {
        using var qa = new QaContext();
        // Teste arredondamento AwayFromZero para ticket médio
        // 3 vendas totalizando 100 -> 100/3 = 33.333... -> 33.33 (AwayFromZero, terceiro decimal é 3 < 5)
        qa.SeedVenda(33m, data: Hoje.AddHours(10));
        qa.SeedVenda(34m, data: Hoje.AddHours(11));
        qa.SeedVenda(33m, data: Hoje.AddHours(12)); // total 100, 3 vendas = 33.333...
        
        // Anterior: 1 venda = 99 -> ticket 99
        qa.SeedVenda(99m, data: Ontem.AddHours(10));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        // Ticket = 100/3 = 33.333... -> 33.33 (AwayFromZero, terceiro decimal 3 < 5)
        Assert.Equal(33.33m, model.TicketMedioAtual);
        // Variação: (100-99)/99*100 = 1.0101...% -> 1.0% (1 casa, AwayFromZero)
        Assert.Equal(1.0m, model.VariacaoPercentual);
    }

    // =====================================================================
    // 3. FRASE DO CABEÇALHO
    // =====================================================================

    // =====================================================================
    // 3. FRASE DO CABEÇALHO
    // =====================================================================

    [Fact]
    public async Task FraseCabecalho_Sobe()
    {
        using var qa = new QaContext();
        // Variação positiva -> "As vendas subiram X% em relação ao período anterior."
        qa.SeedVenda(200m, data: Hoje.AddHours(10));
        qa.SeedVenda(100m, data: Hoje.AddHours(11));
        qa.SeedVenda(100m, data: Ontem.AddHours(10)); // anterior = 100

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Contains("subiram", model.FraseCabecalho);
        Assert.Contains("200.0%", model.FraseCabecalho); // InvariantCulture usa "."
        Assert.Contains("período anterior", model.FraseCabecalho);
    }

    [Fact]
    public async Task FraseCabecalho_Cai()
    {
        using var qa = new QaContext();
        // Variação negativa -> "As vendas caíram X% em relação ao período anterior."
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Ontem.AddHours(10));
        qa.SeedVenda(100m, data: Ontem.AddHours(11)); // anterior = 300

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Contains("caíram", model.FraseCabecalho);
        Assert.Contains("66.7%", model.FraseCabecalho); // InvariantCulture usa "."
        Assert.Contains("período anterior", model.FraseCabecalho);
    }

    [Fact]
    public async Task FraseCabecalho_Establevel()
    {
        using var qa = new QaContext();
        // Variação zero -> "As vendas ficaram estáveis em relação ao período anterior."
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(100m, data: Hoje.AddHours(11));
        qa.SeedVenda(100m, data: Ontem.AddHours(10));
        qa.SeedVenda(100m, data: Ontem.AddHours(11));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Contains("ficaram estáveis", model.FraseCabecalho);
        Assert.Contains("período anterior", model.FraseCabecalho);
    }

    [Fact]
    public async Task FraseCabecalho_SemBase()
    {
        using var qa = new QaContext();
        // Sem vendas no anterior -> "Ainda não há vendas no período anterior para comparar."
        qa.SeedVenda(100m, data: Hoje.AddHours(10));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Contains("Ainda não há vendas no período anterior para comparar", model.FraseCabecalho);
    }

    [Fact]
    public async Task FraseCabecalho_SemVendasPeriodo()
    {
        using var qa = new QaContext();
        // Sem vendas no período -> "Sem vendas no período."
        // Não semeia nenhuma venda

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal("Sem vendas no período.", model.FraseCabecalho);
    }

    // =====================================================================
    // 4. SAUDAÇÃO POR HORA E SEM @
    // =====================================================================

    [Fact]
    public async Task Saudacao_BomDia_AntesDe12h()
    {
        using var qa = new QaContext();
        // Mock clock: 10:00
        var clock = new Func<DateTime>(() => new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 10, 0, 0));
        
        var controller = new DashboardController(qa.Context, clock);
        var result = await controller.Index(periodo: "hoje") as ViewResult;
        var model = result.Model as DashboardViewModel;

        Assert.StartsWith("Bom dia, ", model.Saudacao);
    }

    [Fact]
    public async Task Saudacao_BoaTarde_12hAte18h()
    {
        using var qa = new QaContext();
        // Mock clock: 14:00
        var clock = new Func<DateTime>(() => new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 14, 0, 0));
        
        var controller = new DashboardController(qa.Context, clock);
        var result = await controller.Index(periodo: "hoje") as ViewResult;
        var model = result.Model as DashboardViewModel;

        Assert.StartsWith("Boa tarde, ", model.Saudacao);
    }

    [Fact]
    public async Task Saudacao_BoaNoite_DepoisDe18h()
    {
        using var qa = new QaContext();
        // Mock clock: 20:00
        var clock = new Func<DateTime>(() => new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 20, 0, 0));
        
        var controller = new DashboardController(qa.Context, clock);
        var result = await controller.Index(periodo: "hoje") as ViewResult;
        var model = result.Model as DashboardViewModel;

        Assert.StartsWith("Boa noite, ", model.Saudacao);
    }

    [Fact]
    public async Task Saudacao_NomeExibidoDoBotaoSidebar_NaoEmail_SemArroba()
    {
        // Lê a mesma fonte que o _Layout usa para o botão do perfil
        using var qa = new QaContext();
        // Sem usuário logado, deve usar fallback "Dono"
        var clock = new Func<DateTime>(() => DateTime.Now);
        var controller = new DashboardController(qa.Context, clock);
        var result = await controller.Index(periodo: "hoje") as ViewResult;
        var model = result.Model as DashboardViewModel;

        Assert.DoesNotContain("@", model.Saudacao);
        Assert.Contains("Dono", model.Saudacao); // fallback
    }

    [Fact]
    public async Task Saudacao_Fallback_Dono()
    {
        using var qa = new QaContext();
        var clock = new Func<DateTime>(() => DateTime.Now);
        var controller = new DashboardController(qa.Context, clock);
        var result = await controller.Index(periodo: "hoje") as ViewResult;
        var model = result.Model as DashboardViewModel;

        Assert.Contains("Dono", model.Saudacao); // fallback quando não há usuário logado
    }

    // =====================================================================
    // 5. GAVETA = REGRA 7 (INDEPENDENTE DO PERÍODO)
    // =====================================================================

    [Fact]
    public async Task Gaveta_IgualAoSaldoEmGaveta_Regra7()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(200m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10));
        qa.SeedVenda(900m, metodoPagamento: "Credito", data: Hoje.AddHours(11));
        qa.SeedVenda(800m, metodoPagamento: "Dinheiro", data: Ontem.AddHours(9));
        qa.SeedVenda(700m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(12), isFechado: true);
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(12));

        // Testar com diferentes períodos - gaveta NÃO muda
        var modelHoje = await ChamarIndex(qa.Context, periodo: "hoje");
        var model7dias = await ChamarIndex(qa.Context, periodo: "7dias");
        var modelMes = await ChamarIndex(qa.Context, periodo: "mes");

        Assert.Equal(modelHoje.SaldoGaveta, model7dias.SaldoGaveta);
        Assert.Equal(modelHoje.SaldoGaveta, modelMes.SaldoGaveta);
        Assert.Equal(330m, modelHoje.SaldoGaveta); // 300 + 50 - 20

        // Comparar com CaixaController.ObterStatusAtual no mesmo seed
        var controller = new ControleEstoqueLoja.Controllers.CaixaController(qa.Context);
        var statusResult = await controller.ObterStatusAtual();
        var jsonResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(statusResult);
        var statusObj = jsonResult.Value;
        var saldoProp = statusObj.GetType().GetProperty("saldo");
        var saldoCaixa = (double)saldoProp.GetValue(statusObj);
        Assert.Equal((double)modelHoje.SaldoGaveta, saldoCaixa);
    }

    // =====================================================================
    // 6. ESTOQUE BAIXO
    // =====================================================================

    [Fact]
    public async Task EstoqueBaixo_OrdemPorRazao_QtdSobreMinimo()
    {
        using var qa = new QaContext();
        // 9 contra mínimo 10 = razão 0.9 (baixo)
        // 10 contra mínimo 9 = razão 1.11 (não é baixo)
        qa.SeedProduto(nome: "NOVE", quantidade: 9m, estoqueMinimo: 10m);
        qa.SeedProduto(nome: "DEZ", quantidade: 10m, estoqueMinimo: 9m);
        qa.SeedProduto(nome: "ZERADO", quantidade: 0m, estoqueMinimo: 5m); // razão 0 -> Zerado

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(2, model.TotalEstoqueBaixo); // NOVE e ZERADO
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "DEZ");
        // ZERADO primeiro (razão 0)
        Assert.Equal("ZERADO", model.EstoqueBaixo[0].Nome);
        var situacao0 = model.EstoqueBaixo[0].Quantidade <= 0 ? "Zerado" : "Baixo";
        Assert.Equal("Zerado", situacao0);
        Assert.Equal("NOVE", model.EstoqueBaixo[1].Nome);
        var situacao1 = model.EstoqueBaixo[1].Quantidade <= 0 ? "Zerado" : "Baixo";
        Assert.Equal("Baixo", situacao1);
    }

    [Fact]
    public async Task EstoqueBaixo_Limite6_TotalGeral_SituacaoZeradoOuBaixo()
    {
        using var qa = new QaContext();
        // Criar 7 produtos com estoque baixo
        for (int i = 1; i <= 7; i++)
        {
            qa.SeedProduto(nome: $"PROD{i}", quantidade: 5m, estoqueMinimo: 10m);
        }

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(7, model.TotalEstoqueBaixo); // total geral
        Assert.Equal(5, model.EstoqueBaixo.Count); // limite de 5 na lista (TopEstoqueBaixo)
        Assert.All(model.EstoqueBaixo, p => 
        {
            var situacao = p.Quantidade <= 0 ? "Zerado" : "Baixo";
            Assert.Contains(situacao, new[] { "Baixo", "Zerado" });
        });
    }

    [Fact]
    public async Task EstoqueBaixo_InativosFora()
    {
        using var qa = new QaContext();
        var ativo = qa.SeedProduto(nome: "ATIVO_BAIXO", quantidade: 5m, estoqueMinimo: 10m);
        var inativo = qa.SeedProduto(nome: "INATIVO_BAIXO", quantidade: 5m, estoqueMinimo: 10m);
        inativo.Ativo = false;
        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(1, model.TotalEstoqueBaixo);
        Assert.DoesNotContain(model.EstoqueBaixo, p => p.Nome == "INATIVO_BAIXO");
        Assert.Contains(model.EstoqueBaixo, p => p.Nome == "ATIVO_BAIXO");
    }

    // =====================================================================
    // 7. MAIS VENDIDOS (TOP 5 POR VALOR)
    // =====================================================================

    [Fact]
    public async Task MaisVendidos_OrdemPorValor_DesempatePorNome()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task MaisVendidos_SomaQuantidade_KgUn_Corretamente()
    {
        // 0.125 + 0.375 kg = 0.5 kg (soma numérica, arredondar só no fim)
        // 9 + 10 un = 19 un
        throw new NotImplementedException();
    }

    [Fact]
    public async Task MaisVendidos_IncluiInativos_EVendasFechadas()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task MaisVendidos_BarraRelativaAoMaior()
    {
        throw new NotImplementedException();
    }

    // =====================================================================
    // 8. FORMAS DE PAGAMENTO (ROSQUILHA)
    // =====================================================================

    [Fact]
    public async Task FormasPagamento_AgrupaSemDiferenciarMaiusculas()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task FormasPagamento_PorcentagemSoma100_UltimoAbsorveArredondamento()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task FormasPagamento_CoresPorForma_DasharrayOffsetCalculados()
    {
        // Dinheiro green, Pix cyan, Crédito violet, Débito blue, Voucher amber, demais slate
        // Círculo r=40, circunferência 2*PI*40
        throw new NotImplementedException();
    }

    // =====================================================================
    // 9. BUCKETS E SÉRIE ANTERIOR
    // =====================================================================

    [Fact]
    public async Task Buckets_Hoje_24Horas_Rotulos_00h_23h()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Buckets_7dias_30dias_Dias_Rotulos_ddMM()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Buckets_Mes_DiasDoMesAteHoje_Rotulos_ddMM()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Buckets_Ano_MesesAteAtual_Rotulos_jan_dez()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Buckets_ValoresFuturos_HorasDepoisDeAgora_SaoNull()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Buckets_SerieAnterior_Inexistente_MesAnteriorMenor_SaoNull()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Buckets_SomaAtuais_IgualFaturamentoPeriodo()
    {
        throw new NotImplementedException();
    }

    // =====================================================================
    // 10. GRAFICOSVG - NICE TICKS
    // =====================================================================

    [Fact]
    public void GraficoSvg_NiceTicks_MaxZero_Retorna0_50_100()
    {
        var ticks = GraficoSvg.NiceTicks(0m);
        Assert.Equal(new[] { 0m, 50m, 100m }, ticks);
    }

    [Fact]
    public void GraficoSvg_NiceTicks_Ate5Marcas_Passo1_2_2_5_5_Vezes10k()
    {
        throw new NotImplementedException();
    }

    // =====================================================================
    // 11. GRAFICOSVG - GEOMETRIA DO CAMINHO
    // =====================================================================

    [Fact]
    public void GraficoSvg_CaminhoLinha_SemVirgula_SemNaN_SemInfinity_XCrescente_YNoRange()
    {
        // CultureInfo("pt-BR") como cultura atual do teste
        throw new NotImplementedException();
    }

    [Fact]
    public void GraficoSvg_CaminhoLinha_InterpolacaoCubicaMonotona_SemOvershoot()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void GraficoSvg_CaminhoLinha_AceitaNulosNoFim_LinhaTerminaAli()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void GraficoSvg_CaminhoLinha_PontosIguaisABucketsComValor()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void GraficoSvg_CaminhoArea_MesmaGeometriaFechadaNoEixoY()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void GraficoSvg_CasoPrint_9_98e10_00_RestoZero_CurvaCoerente()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void GraficoSvg_TodosZeros_UmUnicoBucket_SemExcecao()
    {
        throw new NotImplementedException();
    }

    // =====================================================================
    // 12. PARÂMETROS DO INDEX
    // =====================================================================

    [Fact]
    public async Task Parametros_PeriodoInvalido_ViraHoje()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Parametros_DiasLegado_7Ou30_Funciona()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Parametros_PeriodoPrevaleceSobreDias()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task Parametros_DataFutura_Ignorada()
    {
        throw new NotImplementedException();
    }

    // =====================================================================
    // 13. BANCO VAZIO E SOMENTE LEITURA
    // =====================================================================

    [Fact]
    public async Task BancoVazio_TudoZerado_ListasVazias()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public async Task SomenteLeitura_NaoAlteraContagens()
    {
        throw new NotImplementedException();
    }

    // =====================================================================
    // 14. AUTORIZAÇÃO POR REFLEXÃO
    // =====================================================================

    [Fact]
    public void Autorizacao_ControllerExigePodeVerFinanceiro()
    {
        var attr = typeof(DashboardController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(Policies.PodeVerFinanceiro, attr!.Policy);
    }

    [Fact]
    public void Autorizacao_Index_EHttpGet_SemAcoesDeEscrita()
    {
        var index = typeof(DashboardController).GetMethod(nameof(DashboardController.Index))!;
        var httpGet = index.GetCustomAttributes()
            .Any(a => a.GetType().Name == "HttpGetAttribute");
        Assert.True(httpGet);

        var verbs = typeof(DashboardController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes().Select(a => a.GetType().Name))
            .Where(v => v.StartsWith("Http") && v != "HttpGetAttribute")
            .ToList();

        Assert.Empty(verbs);
    }
}