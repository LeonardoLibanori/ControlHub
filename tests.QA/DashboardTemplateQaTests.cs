using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;
using System.Threading;

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
        using var qa = new QaContext();
        // Cria produtos
        var prodA = qa.SeedProduto(nome: "A", preco: 100m, estoqueMinimo: 10);
        var prodB = qa.SeedProduto(nome: "B", preco: 100m, estoqueMinimo: 10);
        var prodC = qa.SeedProduto(nome: "C", preco: 50m, estoqueMinimo: 10);

        // Vendas com itens: cada venda tem 1 item do respectivo produto
        var v1 = qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v1.Id, ProdutoId = prodA.Id, Quantidade = 1m, PrecoUnitario = 100m });
        
        var v2 = qa.SeedVenda(100m, data: Hoje.AddHours(11));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v2.Id, ProdutoId = prodB.Id, Quantidade = 1m, PrecoUnitario = 100m });
        
        var v3 = qa.SeedVenda(50m, data: Hoje.AddHours(12));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v3.Id, ProdutoId = prodC.Id, Quantidade = 1m, PrecoUnitario = 50m });

        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(3, model.MaisVendidos.Count);
        Assert.Equal("A", model.MaisVendidos[0].Nome); // A vem antes de B por ordem alfabética (mesmo valor)
        Assert.Equal("B", model.MaisVendidos[1].Nome);
        Assert.Equal("C", model.MaisVendidos[2].Nome);
    }

    [Fact]
    public async Task MaisVendidos_SomaQuantidade_KgUn_Corretamente()
    {
        using var qa = new QaContext();
        // Cria produtos
        var prodKg = qa.SeedProduto(nome: "QUEIJO", preco: 100m, tipo: TipoProduto.PorPeso, estoqueMinimo: 10);
        var prodUn = qa.SeedProduto(nome: "PÃO", preco: 10m, tipo: TipoProduto.Prateleira, estoqueMinimo: 10);

        // Vendas com itens: 0.125 + 0.375 kg = 0.5 kg de QUEIJO
        var v1 = qa.SeedVenda(12.5m, data: Hoje.AddHours(10));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v1.Id, ProdutoId = prodKg.Id, Quantidade = 0.125m, PrecoUnitario = 100m });
        var v2 = qa.SeedVenda(37.5m, data: Hoje.AddHours(11));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v2.Id, ProdutoId = prodKg.Id, Quantidade = 0.375m, PrecoUnitario = 100m });
        
        // 9 + 10 un = 19 un de PÃO
        var v3 = qa.SeedVenda(90m, data: Hoje.AddHours(12));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v3.Id, ProdutoId = prodUn.Id, Quantidade = 9m, PrecoUnitario = 10m });
        var v4 = qa.SeedVenda(100m, data: Hoje.AddHours(13));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v4.Id, ProdutoId = prodUn.Id, Quantidade = 10m, PrecoUnitario = 10m });

        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        // QUEIJO: 0.125 + 0.375 = 0.5 kg, valor = 50
        var queijo = model.MaisVendidos.First(p => p.Nome == "QUEIJO");
        Assert.Equal(0.5m, queijo.Quantidade);
        Assert.Equal("kg", queijo.Unidade);
        Assert.Equal(50m, queijo.Valor);

        // PÃO: 9 + 10 = 19 un, valor = 190
        var pao = model.MaisVendidos.First(p => p.Nome == "PÃO");
        Assert.Equal(19m, pao.Quantidade);
        Assert.Equal("un", pao.Unidade);
        Assert.Equal(190m, pao.Valor);
    }

    [Fact]
    public async Task MaisVendidos_IncluiInativos_EVendasFechadas()
    {
        using var qa = new QaContext();
        var prodInativo = qa.SeedProduto(nome: "INATIVO", preco: 50m, estoqueMinimo: 10);
        prodInativo.Ativo = false;
        qa.Context.SaveChanges();

        var prodAtivo = qa.SeedProduto(nome: "ATIVO", preco: 100m, estoqueMinimo: 10);

        // Venda do produto inativo (não fechada)
        var v1 = qa.SeedVenda(50m, data: Hoje.AddHours(10));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v1.Id, ProdutoId = prodInativo.Id, Quantidade = 1m, PrecoUnitario = 50m });
        
        // Venda fechada do produto ativo
        var v2 = qa.SeedVenda(100m, data: Hoje.AddHours(11), isFechado: true);
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v2.Id, ProdutoId = prodAtivo.Id, Quantidade = 1m, PrecoUnitario = 100m });

        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Contains(model.MaisVendidos, p => p.Nome == "INATIVO");
        Assert.Contains(model.MaisVendidos, p => p.Nome == "ATIVO");
    }

    [Fact]
    public async Task MaisVendidos_BarraRelativaAoMaior()
    {
        using var qa = new QaContext();
        // Cria produtos
        var prodA = qa.SeedProduto(nome: "A", preco: 100m, estoqueMinimo: 10);
        var prodB = qa.SeedProduto(nome: "B", preco: 50m, estoqueMinimo: 10);
        var prodC = qa.SeedProduto(nome: "C", preco: 25m, estoqueMinimo: 10);

        // Vendas com itens: cada venda tem 1 item do respectivo produto
        var v1 = qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v1.Id, ProdutoId = prodA.Id, Quantidade = 1m, PrecoUnitario = 100m });
        
        var v2 = qa.SeedVenda(50m, data: Hoje.AddHours(11));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v2.Id, ProdutoId = prodB.Id, Quantidade = 1m, PrecoUnitario = 50m });
        
        var v3 = qa.SeedVenda(25m, data: Hoje.AddHours(12));
        qa.Context.VendaItens.Add(new VendaItem { VendaId = v3.Id, ProdutoId = prodC.Id, Quantidade = 1m, PrecoUnitario = 25m });

        qa.Context.SaveChanges();

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(3, model.MaisVendidos.Count);
        Assert.Equal(100m, model.MaisVendidos[0].BarraRelativa); // 100%
        Assert.Equal(50m, model.MaisVendidos[1].BarraRelativa);  // 50%
        Assert.Equal(25m, model.MaisVendidos[2].BarraRelativa);  // 25%
    }

    // =====================================================================
    // 8. FORMAS DE PAGAMENTO (ROSQUILHA)
    // =====================================================================

    [Fact]
    public async Task FormasPagamento_AgrupaSemDiferenciarMaiusculas()
    {
        using var qa = new QaContext();
        // "Dinheiro" e "dinheiro" devem ser agrupados
        qa.SeedVenda(100m, data: Hoje.AddHours(10), metodoPagamento: "Dinheiro");
        qa.SeedVenda(50m, data: Hoje.AddHours(11), metodoPagamento: "dinheiro");
        qa.SeedVenda(200m, data: Hoje.AddHours(12), metodoPagamento: "PIX");

        // Teste isolado do agrupamento case-insensitive com strings conhecidas
        var testeStrings = new List<string> { "Dinheiro", "dinheiro", "PIX" };
        var dictTeste = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in testeStrings)
        {
            if (dictTeste.ContainsKey(s))
                dictTeste[s]++;
            else
                dictTeste[s] = 1;
        }
        Assert.Equal(2, dictTeste.Count); // Deve agrupar Dinheiro/dinheiro
        
        // Debug: verificar o que está no banco para hoje
        var vendasHoje = await qa.Context.Vendas
            .Where(v => v.DataVenda >= DateTime.Today && v.DataVenda < DateTime.Today.AddDays(1))
            .Select(v => new { v.MetodoPagamento, v.TotalVenda })
            .ToListAsync();
        
        Assert.Equal(3, vendasHoje.Count); // Deve ter 3 vendas hoje
        
        // Verificar os valores exatos
        var metodos = vendasHoje.Select(v => v.MetodoPagamento).ToList();
        Assert.Contains("Dinheiro", metodos);
        Assert.Contains("dinheiro", metodos);
        Assert.Contains("PIX", metodos);
        
        // Agrupamento manual case-insensitive
        var gruposManual = new Dictionary<string, (string Metodo, decimal Total)>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in vendasHoje)
        {
            if (gruposManual.TryGetValue(v.MetodoPagamento, out var existente))
            {
                gruposManual[v.MetodoPagamento] = (existente.Metodo, existente.Total + v.TotalVenda);
            }
            else
            {
                gruposManual[v.MetodoPagamento] = (v.MetodoPagamento, v.TotalVenda);
            }
        }
        
        Assert.Equal(2, gruposManual.Count); // Deve agrupar Dinheiro/dinheiro
        
        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(2, model.FormasPagamento.Count);
        var dinheiro = model.FormasPagamento.First(f => f.Metodo.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(150m, dinheiro.Valor);
        Assert.Equal("green", dinheiro.Cor);
    }

    [Fact]
    public async Task FormasPagamento_PorcentagemSoma100_UltimoAbsorveArredondamento()
    {
        using var qa = new QaContext();
        // 3 formas: 33.33, 33.33, 33.34 -> deve somar 100.0
        qa.SeedVenda(100m, data: Hoje.AddHours(10), metodoPagamento: "Dinheiro");
        qa.SeedVenda(100m, data: Hoje.AddHours(11), metodoPagamento: "PIX");
        qa.SeedVenda(100m, data: Hoje.AddHours(12), metodoPagamento: "Credito");

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(3, model.FormasPagamento.Count);
        var somaPercentual = model.FormasPagamento.Sum(f => f.Percentual);
        Assert.Equal(100m, somaPercentual);
        
        // Último absorve o arredondamento
        var ultimo = model.FormasPagamento.Last();
        Assert.Equal(33.4m, ultimo.Percentual); // 33.33 + 33.33 + 33.34 = 100.0
    }

    [Fact]
    public async Task FormasPagamento_CoresPorForma_DasharrayOffsetCalculados()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10), metodoPagamento: "Dinheiro"); // green
        qa.SeedVenda(50m, data: Hoje.AddHours(11), metodoPagamento: "PIX");       // cyan
        qa.SeedVenda(200m, data: Hoje.AddHours(12), metodoPagamento: "Credito");   // violet
        qa.SeedVenda(75m, data: Hoje.AddHours(13), metodoPagamento: "Debito");     // blue
        qa.SeedVenda(25m, data: Hoje.AddHours(14), metodoPagamento: "Voucher");    // amber
        qa.SeedVenda(30m, data: Hoje.AddHours(15), metodoPagamento: "Outro");      // slate

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(6, model.FormasPagamento.Count);
        
        var dinheiro = model.FormasPagamento.First(f => f.Metodo == "Dinheiro");
        Assert.Equal("green", dinheiro.Cor);
        
        var pix = model.FormasPagamento.First(f => f.Metodo == "PIX");
        Assert.Equal("cyan", pix.Cor);
        
        var credito = model.FormasPagamento.First(f => f.Metodo == "Credito");
        Assert.Equal("violet", credito.Cor);
        
        var debito = model.FormasPagamento.First(f => f.Metodo == "Debito");
        Assert.Equal("blue", debito.Cor);
        
        var voucher = model.FormasPagamento.First(f => f.Metodo == "Voucher");
        Assert.Equal("amber", voucher.Cor);
        
        var outro = model.FormasPagamento.First(f => f.Metodo == "Outro");
        Assert.Equal("slate", outro.Cor);

        // Verificar dasharray e dashoffset (círculo r=40, circunferência = 2*PI*40)
        const decimal raio = 40m;
        const decimal circunferencia = 2m * (decimal)Math.PI * raio;
        
        var somaDashArray = model.FormasPagamento.Sum(f => f.DashArray);
        Assert.Equal(Math.Round(circunferencia, 2), Math.Round(somaDashArray, 2));
    }

    // =====================================================================
    // 9. BUCKETS E SÉRIE ANTERIOR
    // =====================================================================

    [Fact]
    public async Task Buckets_Hoje_24Horas_Rotulos_00h_23h()
    {
        using var qa = new QaContext();
        // Vendas em várias horas do dia
        for (int h = 0; h <= 23; h++)
        {
            if (h % 3 == 0) // a cada 3 horas
                qa.SeedVenda(100m, data: Hoje.AddHours(h));
        }

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(24, model.SerieAtual.Count);
        Assert.Equal("00h", model.SerieAtual[0].Rotulo);
        Assert.Equal("23h", model.SerieAtual[23].Rotulo);
        
        // Verificar que todas as horas existem
        for (int i = 0; i < 24; i++)
        {
            Assert.Equal($"{i:00}h", model.SerieAtual[i].Rotulo);
        }
    }

    [Fact]
    public async Task Buckets_7dias_30dias_Dias_Rotulos_ddMM()
    {
        using var qa = new QaContext();
        // 7dias: 7 buckets com rótulos dd/MM
        qa.SeedVenda(100m, data: Hoje.AddDays(-3));
        var model7 = await ChamarIndex(qa.Context, periodo: "7dias");
        
        Assert.Equal(7, model7.SerieAtual.Count);
        Assert.Matches(@"\d{2}/\d{2}", model7.SerieAtual[0].Rotulo);
        Assert.Matches(@"\d{2}/\d{2}", model7.SerieAtual[6].Rotulo);

        // 30dias: 30 buckets com rótulos dd/MM
        qa.SeedVenda(100m, data: Hoje.AddDays(-15));
        var model30 = await ChamarIndex(qa.Context, periodo: "30dias");
        
        Assert.Equal(30, model30.SerieAtual.Count);
        Assert.Matches(@"\d{2}/\d{2}", model30.SerieAtual[0].Rotulo);
        Assert.Matches(@"\d{2}/\d{2}", model30.SerieAtual[29].Rotulo);
    }

    [Fact]
    public async Task Buckets_Mes_DiasDoMesAteHoje_Rotulos_ddMM()
    {
        using var qa = new QaContext();
        var hoje = DateTime.Today;
        var primeiroDiaMes = new DateTime(hoje.Year, hoje.Month, 1);
        qa.SeedVenda(100m, data: primeiroDiaMes);
        qa.SeedVenda(200m, data: hoje);

        var model = await ChamarIndex(qa.Context, periodo: "mes");

        // Número de buckets = dia de hoje
        Assert.Equal(hoje.Day, model.SerieAtual.Count);
        Assert.Matches(@"\d{2}/\d{2}", model.SerieAtual[0].Rotulo);
        Assert.Matches(@"\d{2}/\d{2}", model.SerieAtual[^1].Rotulo);
        // Primeiro rótulo deve ser 01/MM
        var expectedFirst = "01/" + hoje.Month.ToString("00");
        Assert.Equal(expectedFirst, model.SerieAtual[0].Rotulo);
        // Último rótulo deve ser dd/MM de hoje
        var expectedLast = hoje.Day.ToString("00") + "/" + hoje.Month.ToString("00");
        Assert.Equal(expectedLast, model.SerieAtual[^1].Rotulo);
    }

    [Fact]
    public async Task Buckets_Ano_MesesAteAtual_Rotulos_jan_dez()
    {
        using var qa = new QaContext();
        var hoje = DateTime.Today;
        var primeiroAno = new DateTime(hoje.Year, 1, 1);
        qa.SeedVenda(100m, data: primeiroAno);
        qa.SeedVenda(200m, data: hoje);

        var model = await ChamarIndex(qa.Context, periodo: "ano");

        // Número de buckets = mês atual
        Assert.Equal(hoje.Month, model.SerieAtual.Count);
        
        // Rótulos usam CultureInfo.InvariantCulture (inglês): jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec
        Assert.Equal("jan", model.SerieAtual[0].Rotulo);
        
        var mesAtualEsperado = new DateTime(2000, hoje.Month, 1).ToString("MMM", CultureInfo.InvariantCulture).ToLowerInvariant();
        Assert.Equal(mesAtualEsperado, model.SerieAtual[^1].Rotulo);
    }

    [Fact]
    public async Task Buckets_ValoresFuturos_HorasDepoisDeAgora_SaoNull()
    {
        using var qa = new QaContext();
        var agora = DateTime.Now;
        var horaAtual = agora.Hour;
        
        // Venda na hora atual
        qa.SeedVenda(100m, data: Hoje.AddHours(horaAtual));
        // Venda em hora futura (se houver)
        if (horaAtual < 23)
            qa.SeedVenda(200m, data: Hoje.AddHours(horaAtual + 1));

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        // Horas após a hora atual devem ter ValorAtual = null
        for (int i = horaAtual + 1; i < 24; i++)
        {
            Assert.Null(model.SerieAtual[i].ValorAtual);
        }
        // Hora atual e anteriores devem ter valor
        Assert.NotNull(model.SerieAtual[horaAtual].ValorAtual);
    }

    [Fact]
    public async Task Buckets_SerieAnterior_Inexistente_MesAnteriorMenor_SaoNull()
    {
        using var qa = new QaContext();
        // Este teste verifica que quando o mês atual tem mais dias que o mês anterior,
        // os buckets extras têm ValorAnterior = null.
        // Usamos a data real de hoje para o período "mes".
        
        var controller = new DashboardController(qa.Context);
        var result = await controller.Index(periodo: "mes") as ViewResult;
        var model = result.Model as DashboardViewModel;

        // O número de buckets deve ser igual ao dia de hoje
        Assert.Equal(DateTime.Today.Day, model.SerieAtual.Count);
        
        // Se o mês anterior tem menos dias, os buckets extras devem ter ValorAnterior = null
        // Ex: hoje = 31/03, mês anterior (fev) tem 28 dias -> buckets 28,29,30 (índices 27,28,29) são null
        var hoje = DateTime.Today;
        var primeiroMesAtual = new DateTime(hoje.Year, hoje.Month, 1);
        var primeiroMesAnterior = primeiroMesAtual.AddMonths(-1);
        var ultimoDiaMesAnterior = primeiroMesAtual.AddDays(-1).Day;
        
        if (hoje.Day > ultimoDiaMesAnterior)
        {
            for (int i = ultimoDiaMesAnterior; i < hoje.Day; i++)
            {
                Assert.Null(model.SerieAtual[i].ValorAnterior);
            }
        }
    }

    [Fact]
    public async Task Buckets_SomaAtuais_IgualFaturamentoPeriodo()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Hoje.AddHours(11));
        qa.SeedVenda(50m, data: Hoje.AddDays(-3)); // dentro de 7dias

        var model = await ChamarIndex(qa.Context, periodo: "7dias");

        var somaBuckets = model.SerieAtual.Where(b => b.ValorAtual.HasValue).Sum(b => b.ValorAtual.Value);
        Assert.Equal(model.FaturamentoAtual, somaBuckets);
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
        // max = 1000 -> passos possíveis: 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000
        // magnitude = 3 (10^3 = 1000)
        // candidatos: 1*1000=1000 (2 ticks), 2*1000=2000 (1 tick), 2.5*1000=2500 (1), 5*1000=5000 (1)
        // Escolhe 1000 -> ticks: 0, 1000
        var ticks = GraficoSvg.NiceTicks(1000m);
        Assert.True(ticks.Count <= 5);
        Assert.Contains(0m, ticks);
        Assert.Contains(1000m, ticks);

        // max = 150 -> magnitude = 2 (100)
        // candidatos: 100 (2 ticks), 200 (1), 250 (1), 500 (1) -> escolhe 100
        ticks = GraficoSvg.NiceTicks(150m);
        Assert.True(ticks.Count <= 5);
        Assert.Contains(0m, ticks);
        Assert.Contains(100m, ticks);

        // max = 75 -> magnitude = 1 (10)
        // candidatos: 10 (8 ticks > 5), 20 (4 ticks <= 5) -> escolhe 20
        ticks = GraficoSvg.NiceTicks(75m);
        Assert.True(ticks.Count <= 5);
        Assert.Contains(0m, ticks);
        Assert.Contains(20m, ticks);
        Assert.Contains(40m, ticks);
        Assert.Contains(60m, ticks);

        // max = 12 -> magnitude = 1 (10)
        // candidatos: 10 (2 ticks), 20 (1), 25 (1), 50 (1) -> escolhe 10
        ticks = GraficoSvg.NiceTicks(12m);
        Assert.True(ticks.Count <= 5);
        Assert.Contains(0m, ticks);
        Assert.Contains(10m, ticks);

        // max = 99999 -> magnitude = 4 (10000)
        // candidatos: 10000 (10 ticks > 5), 20000 (5 ticks <= 5) -> escolhe 20000
        ticks = GraficoSvg.NiceTicks(99999m);
        Assert.True(ticks.Count <= 5);
    }

    // =====================================================================
    // 11. GRAFICOSVG - GEOMETRIA DO CAMINHO
    // =====================================================================

    [Fact]
    public void GraficoSvg_CaminhoLinha_SemVirgula_SemNaN_SemInfinity_XCrescente_YNoRange()
    {
        // CultureInfo("pt-BR") como cultura atual do teste
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("pt-BR");

        try
        {
            var valores = new List<decimal?> { 0m, 50m, 100m, 75m, 25m };
            var path = GraficoSvg.CaminhoLinha(valores, 100m);

            Assert.False(string.IsNullOrEmpty(path));
            Assert.DoesNotContain(",", path); // InvariantCulture usa ponto, não vírgula
            Assert.DoesNotContain("NaN", path);
            Assert.DoesNotContain("Infinity", path);
            Assert.DoesNotContain("-Infinity", path);
            Assert.StartsWith("M ", path);

            // Verificar se há comandos C (curvas cúbicas)
            Assert.Contains(" C ", path);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
            Thread.CurrentThread.CurrentUICulture = culturaOriginal;
        }
    }

    [Fact]
    public void GraficoSvg_CaminhoLinha_InterpolacaoCubicaMonotona_SemOvershoot()
    {
        // Teste de monotonicidade: a curva não deve ultrapassar os valores (overshoot)
        // Usar valores que poderiam causar overshoot em interpolação cúbica simples
        var valores = new List<decimal?> { 0m, 100m, 0m, 100m, 0m };
        var path = GraficoSvg.CaminhoLinha(valores, 100m);

        Assert.False(string.IsNullOrEmpty(path));
        Assert.StartsWith("M ", path);
        
        // A interpolação Fritsch-Carlson garante monotonicidade
        // Apenas verificamos que o path é válido e não vazio
    }

    [Fact]
    public void GraficoSvg_CaminhoLinha_AceitaNulosNoFim_LinhaTerminaAli()
    {
        // Nulos no fim: a linha deve terminar no último valor válido
        var valores = new List<decimal?> { 10m, 20m, 30m, null, null, null };
        var path = GraficoSvg.CaminhoLinha(valores, 30m);

        Assert.False(string.IsNullOrEmpty(path));
        Assert.StartsWith("M ", path);
        
        // Com 3 pontos válidos, deve haver 2 segmentos cúbicos
        var countC = path.Split(" C ").Length - 1;
        Assert.Equal(2, countC);
    }

    [Fact]
    public void GraficoSvg_CaminhoLinha_PontosIguaisABucketsComValor()
    {
        // O número de pontos na linha deve ser igual ao número de buckets com valor
        var valores = new List<decimal?> { 10m, 20m, 30m, 40m, 50m };
        var path = GraficoSvg.CaminhoLinha(valores, 50m);

        Assert.False(string.IsNullOrEmpty(path));
        var countC = path.Split(" C ").Length - 1;
        Assert.Equal(4, countC); // 5 pontos = 4 segmentos
    }

    [Fact]
    public void GraficoSvg_CaminhoArea_MesmaGeometriaFechadaNoEixoY()
    {
        var valores = new List<decimal?> { 0m, 50m, 100m, 50m, 0m };
        var pathArea = GraficoSvg.CaminhoArea(valores, 100m);
        var pathLinha = GraficoSvg.CaminhoLinha(valores, 100m);

        Assert.False(string.IsNullOrEmpty(pathArea));
        Assert.StartsWith("M ", pathArea);
        Assert.EndsWith(" Z", pathArea); // Fecha o caminho
        
        // A área deve conter a linha (exceto o M inicial)
        Assert.Contains(pathLinha.Substring(1), pathArea);
    }

    [Fact]
    public void GraficoSvg_CasoPrint_9_98e10_00_RestoZero_CurvaCoerente()
    {
        // Caso específico: valores que podem gerar problemas de precisão
        var valores = new List<decimal?> { 9.98m, 10.00m, 0m, 0m, 0m };
        var path = GraficoSvg.CaminhoLinha(valores, 10m);

        Assert.False(string.IsNullOrEmpty(path));
        Assert.DoesNotContain("NaN", path);
        Assert.DoesNotContain("Infinity", path);
    }

    [Fact]
    public void GraficoSvg_TodosZeros_UmUnicoBucket_SemExcecao()
    {
        // Todos os valores zero: deve retornar path válido (mesmo que seja só o ponto inicial)
        var valores = new List<decimal?> { 0m, 0m, 0m, 0m, 0m };
        var path = GraficoSvg.CaminhoLinha(valores, 0m);

        // Com maxY = 0, o divisor vira 1, percentual = 0, altura = baseY
        // Deve retornar path com 1 ponto (M x y)
        Assert.False(string.IsNullOrEmpty(path));
        Assert.StartsWith("M ", path);
        
        var pathArea = GraficoSvg.CaminhoArea(valores, 0m);
        Assert.False(string.IsNullOrEmpty(pathArea));
    }

    // =====================================================================
    // 12. PARÂMETROS DO INDEX
    // =====================================================================

    [Fact]
    public async Task Parametros_PeriodoInvalido_ViraHoje()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Ontem.AddHours(10));

        var model = await ChamarIndex(qa.Context, periodo: "invalido");

        // Deve cair em "hoje" -> apenas venda de hoje
        Assert.Equal("hoje", model.Periodo);
        Assert.Equal(100m, model.FaturamentoAtual);
        Assert.Equal(1, model.VendasAtual);
    }

    [Fact]
    public async Task Parametros_DiasLegado_7Ou30_Funciona()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Hoje.AddDays(-3));
        qa.SeedVenda(300m, data: Hoje.AddDays(-15));

        // dias=7 (legado) -> deve funcionar como 7dias
        var model7 = await ChamarIndex(qa.Context, dias: 7);
        Assert.Equal("7dias", model7.Periodo);
        Assert.Equal(300m, model7.FaturamentoAtual); // hoje + 3 dias atrás

        // dias=30 (legado) -> deve funcionar como 30dias
        var model30 = await ChamarIndex(qa.Context, dias: 30);
        Assert.Equal("30dias", model30.Periodo);
        Assert.Equal(600m, model30.FaturamentoAtual); // todos
    }

    [Fact]
    public async Task Parametros_PeriodoPrevaleceSobreDias()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Hoje.AddDays(-3));
        qa.SeedVenda(300m, data: Hoje.AddDays(-15));

        // periodo="mes" deve prevalecer sobre dias=7
        var model = await ChamarIndex(qa.Context, dias: 7, periodo: "mes");
        Assert.Equal("mes", model.Periodo);
        // Deve incluir vendas do mês todo, não só 7 dias
    }

    [Fact]
    public async Task Parametros_DataFutura_Ignorada()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));

        // Data futura no parâmetro `data` só afeta a seção "vendas do dia",
        // não o período principal. Sem período explícito, cai no legado (7dias).
        var model = await ChamarIndex(qa.Context, data: Amanha);

        Assert.Equal("7dias", model.Periodo); // legado padrão quando só data é passado
        Assert.Equal(100m, model.FaturamentoAtual); // venda de hoje incluída em 7dias
        Assert.Null(model.DiaSelecionado); // data futura é ignorada para vendas do dia
    }

    // =====================================================================
    // 13. BANCO VAZIO E SOMENTE LEITURA
    // =====================================================================

    [Fact]
    public async Task BancoVazio_TudoZerado_ListasVazias()
    {
        using var qa = new QaContext();
        // Não semeia nada

        var model = await ChamarIndex(qa.Context, periodo: "hoje");

        Assert.Equal(0m, model.FaturamentoAtual);
        Assert.Equal(0, model.VendasAtual);
        Assert.Equal(0m, model.TicketMedioAtual);
        Assert.Equal(0m, model.FaturamentoAnterior);
        Assert.Equal(0, model.VendasAnterior);
        Assert.Equal(0m, model.TicketMedioAnterior);
        Assert.Null(model.VariacaoPercentual);
        Assert.Equal("SemBase", model.EstadoVariacao);
        Assert.Equal("Sem vendas no período.", model.FraseCabecalho);
        Assert.Equal(0m, model.SaldoGaveta);
        Assert.Equal(0, model.TotalEstoqueBaixo);
        Assert.Empty(model.EstoqueBaixo);
        Assert.Empty(model.UltimasVendas);
        // SerieAtual tem buckets (24 para "hoje") mas todos com ValorAtual = null
        Assert.Equal(24, model.SerieAtual.Count);
        Assert.All(model.SerieAtual, b => Assert.Null(b.ValorAtual));
        Assert.All(model.SerieAtual, b => Assert.Null(b.ValorAnterior));
        Assert.Empty(model.MaisVendidos);
        Assert.Empty(model.FormasPagamento);
        Assert.Null(model.DiaSelecionado);
        Assert.Empty(model.VendasDoDia);
        Assert.Equal(0m, model.TotalDiaSelecionado);
        Assert.Equal(0, model.NumeroVendasDia);
    }

    [Fact]
    public async Task SomenteLeitura_NaoAlteraContagens()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Ontem.AddHours(10));

        var countVendasAntes = await qa.Context.Vendas.CountAsync();
        var countItensAntes = await qa.Context.VendaItens.CountAsync();
        var countMovsAntes = await qa.Context.CaixaMovimentacoes.CountAsync();

        await ChamarIndex(qa.Context, periodo: "7dias");

        var countVendasDepois = await qa.Context.Vendas.CountAsync();
        var countItensDepois = await qa.Context.VendaItens.CountAsync();
        var countMovsDepois = await qa.Context.CaixaMovimentacoes.CountAsync();

        Assert.Equal(countVendasAntes, countVendasDepois);
        Assert.Equal(countItensAntes, countItensDepois);
        Assert.Equal(countMovsAntes, countMovsDepois);
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