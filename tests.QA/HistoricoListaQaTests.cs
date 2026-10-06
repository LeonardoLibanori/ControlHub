using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// Testes da feature de Histórico de Vendas:
///   - Janelas com fim exclusivo:
///       hoje   = [Hoje, Amanhã)
///       7dias  = [Hoje-6, Amanhã)
///       mes    = [1º do mês, Amanhã)
///       ano    = [1º jan, Amanhã)
///       avulsa = [d, d+1)
///   - Sem parâmetros = TODAS as vendas (título "Todas as vendas", sem cartão dourado, campo data vazio)
///   - periodo=hoje|7dias|mes|ano destaca cartão e usa a MESMA janela
///   - periodo desconhecido, data futura ou ModelState inválido caem em hoje
///   - Data prevalece sobre período
///   - Limite de 200 linhas com aviso, total e contagem sobre todas
///   - Totais em C# com decimal sobre TotalVenda gravado
///   - Somente leitura
/// </summary>
public class HistoricoListaQaTests
{
    private static DateTime Hoje => DateTime.Today;
    private static DateTime Ontem => DateTime.Today.AddDays(-1);
    private static DateTime Amanha => DateTime.Today.AddDays(1);

    // =========================================================================
    // 1. HOJE EXCLUI ONTEM
    // =========================================================================
    [Fact]
    public async Task Historico_PeriodoHoje_ExcluiOntem()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10), codigoVendedor: "A");
        qa.SeedVenda(50m, data: Ontem.AddHours(15), codigoVendedor: "B");

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null, "hoje");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Single(model);
        Assert.Equal(100m, model[0].TotalVenda);
        Assert.Equal("Vendas de hoje", view.ViewData["Titulo"]);
        Assert.Equal("hoje", view.ViewData["Periodo"]);
    }

    // =========================================================================
    // 2. 7 DIAS COM FRONTEIRAS: Hoje-6 00:00 dentro e Hoje-7 23:59:59.9999999 fora
    // =========================================================================
    [Fact]
    public async Task Historico_Periodo7Dias_FronteiraHojeMenos6Dentro_E_HojeMenos7Fora()
    {
        using var qa = new QaContext();
        // Dentro: Hoje-6 às 00:00:00
        var dataInicioExato = Hoje.AddDays(-6);
        qa.SeedVenda(10m, data: dataInicioExato, codigoVendedor: "DENTRO_INICIO");

        // Fora: 1 tick antes de Hoje-6 (Hoje-7 às 23:59:59.9999999)
        var dataUmTickAntes = dataInicioExato.AddTicks(-1);
        qa.SeedVenda(20m, data: dataUmTickAntes, codigoVendedor: "FORA_PASSADO");

        // Dentro: hoje ao meio-dia
        qa.SeedVenda(30m, data: Hoje.AddHours(12), codigoVendedor: "DENTRO_HOJE");

        // Fora: amanhã às 00:00:00 (fim exclusivo)
        qa.SeedVenda(40m, data: Amanha, codigoVendedor: "FORA_FUTURO");

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null, "7dias");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Equal(2, model.Count);
        Assert.Contains(model, v => v.CodigoVendedor == "DENTRO_INICIO");
        Assert.Contains(model, v => v.CodigoVendedor == "DENTRO_HOJE");
        Assert.DoesNotContain(model, v => v.CodigoVendedor == "FORA_PASSADO");
        Assert.DoesNotContain(model, v => v.CodigoVendedor == "FORA_FUTURO");
        Assert.Equal("Vendas dos últimos 7 dias", view.ViewData["Titulo"]);
        Assert.Equal("7dias", view.ViewData["Periodo"]);
    }

    // =========================================================================
    // 3. MÊS E ANO COM VIRADA
    // =========================================================================
    [Fact]
    public async Task Historico_PeriodoMes_RespeitaViradaDoMes()
    {
        using var qa = new QaContext();
        var primeiroDiaMes = new DateTime(Hoje.Year, Hoje.Month, 1);
        var ultimoMomentoMesAnterior = primeiroDiaMes.AddTicks(-1);

        qa.SeedVenda(100m, data: primeiroDiaMes, codigoVendedor: "DENTRO_MES");
        qa.SeedVenda(50m, data: ultimoMomentoMesAnterior, codigoVendedor: "FORA_MES_ANTERIOR");
        qa.SeedVenda(70m, data: Amanha, codigoVendedor: "FORA_AMANHA");

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null, "mes");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Single(model);
        Assert.Equal("DENTRO_MES", model[0].CodigoVendedor);
        Assert.Equal("Vendas do mês", view.ViewData["Titulo"]);
        Assert.Equal("mes", view.ViewData["Periodo"]);
    }

    [Fact]
    public async Task Historico_PeriodoAno_RespeitaViradaDoAno()
    {
        using var qa = new QaContext();
        var primeiroDiaAno = new DateTime(Hoje.Year, 1, 1);
        var ultimoMomentoAnoAnterior = primeiroDiaAno.AddTicks(-1);

        qa.SeedVenda(200m, data: primeiroDiaAno, codigoVendedor: "DENTRO_ANO");
        qa.SeedVenda(150m, data: ultimoMomentoAnoAnterior, codigoVendedor: "FORA_ANO_ANTERIOR");
        qa.SeedVenda(90m, data: Amanha, codigoVendedor: "FORA_AMANHA");

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null, "ano");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Single(model);
        Assert.Equal("DENTRO_ANO", model[0].CodigoVendedor);
        Assert.Equal("Vendas do ano", view.ViewData["Titulo"]);
        Assert.Equal("ano", view.ViewData["Periodo"]);
    }

    // =========================================================================
    // 4. DATA AVULSA COM LIMITES (e prevalência sobre período)
    // =========================================================================
    [Fact]
    public async Task Historico_DataAvulsa_FiltraExatamenteODia_EPrevaleceSobrePeriodo()
    {
        using var qa = new QaContext();
        var diaEscolhido = Hoje.AddDays(-10);

        // Limites: [diaEscolhido, diaEscolhido + 1)
        qa.SeedVenda(10m, data: diaEscolhido, codigoVendedor: "DENTRO_00");
        qa.SeedVenda(20m, data: diaEscolhido.AddDays(1).AddTicks(-1), codigoVendedor: "DENTRO_2359");
        qa.SeedVenda(30m, data: diaEscolhido.AddTicks(-1), codigoVendedor: "FORA_ANTES");
        qa.SeedVenda(40m, data: diaEscolhido.AddDays(1), codigoVendedor: "FORA_DEPOIS");

        var controller = new CaixaController(qa.Context);
        // Passa data avulsa E periodo="7dias": data prevalece!
        var result = await controller.Historico(diaEscolhido, "7dias");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Equal(2, model.Count);
        Assert.Contains(model, v => v.CodigoVendedor == "DENTRO_00");
        Assert.Contains(model, v => v.CodigoVendedor == "DENTRO_2359");
        Assert.DoesNotContain(model, v => v.CodigoVendedor == "FORA_ANTES");
        Assert.DoesNotContain(model, v => v.CodigoVendedor == "FORA_DEPOIS");

        Assert.Equal($"Vendas de {diaEscolhido:dd/MM/yyyy}", view.ViewData["Titulo"]);
        Assert.Equal(diaEscolhido.Date, view.ViewData["DataSelecionada"]);
        Assert.Null(view.ViewData["Periodo"]); // Sem cartão dourado quando filtro é dia avulso
    }

    // =========================================================================
    // 5. TOTAL DO CARTÃO = SOMA DAS LINHAS (Totais em C# com decimal sobre TotalVenda)
    // =========================================================================
    [Fact]
    public async Task Historico_TotalDoCartao_IgualSomaDasLinhasEmCSharp()
    {
        using var qa = new QaContext();
        qa.SeedVenda(10.25m, data: Hoje.AddHours(8));
        qa.SeedVenda(20.50m, data: Hoje.AddHours(14));
        qa.SeedVenda(30.75m, data: Hoje.AddDays(-3)); // dentro de 7dias, mes e ano
        qa.SeedVenda(40.00m, data: Hoje.AddDays(-20)); // dentro de mes e ano (se mesmo mes)
        qa.SeedVenda(50.00m, data: new DateTime(Hoje.Year, 1, 2)); // dentro do ano

        var controller = new CaixaController(qa.Context);

        // Teste para hoje
        var resHoje = await controller.Historico(null, "hoje");
        var viewHoje = Assert.IsType<ViewResult>(resHoje);
        var modelHoje = Assert.IsAssignableFrom<IEnumerable<Venda>>(viewHoje.Model).ToList();
        var faturamentoHoje = Assert.IsType<decimal>(viewHoje.ViewData["FaturamentoHoje"]);
        Assert.Equal(modelHoje.Sum(v => v.TotalVenda), faturamentoHoje);
        Assert.Equal(30.75m, faturamentoHoje);

        // Teste para 7dias
        var res7Dias = await controller.Historico(null, "7dias");
        var view7Dias = Assert.IsType<ViewResult>(res7Dias);
        var model7Dias = Assert.IsAssignableFrom<IEnumerable<Venda>>(view7Dias.Model).ToList();
        var faturamentoSemana = Assert.IsType<decimal>(view7Dias.ViewData["FaturamentoSemana"]);
        Assert.Equal(model7Dias.Sum(v => v.TotalVenda), faturamentoSemana);

        // Teste para ano
        var resAno = await controller.Historico(null, "ano");
        var viewAno = Assert.IsType<ViewResult>(resAno);
        var modelAno = Assert.IsAssignableFrom<IEnumerable<Venda>>(viewAno.Model).ToList();
        var faturamentoAno = Assert.IsType<decimal>(viewAno.ViewData["FaturamentoAno"]);
        Assert.Equal(modelAno.Sum(v => v.TotalVenda), faturamentoAno);
    }

    // =========================================================================
    // 6. ESTADO NU = TODAS AS VENDAS
    // =========================================================================
    [Fact]
    public async Task Historico_SemParametros_RetornaTodasAsVendas_SemCartaoDourado_E_DataVazia()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(9));
        qa.SeedVenda(200m, data: Hoje.AddDays(-30));
        qa.SeedVenda(300m, data: Hoje.AddYears(-2));

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null, null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Equal(3, model.Count);
        Assert.Equal("Todas as vendas", view.ViewData["Titulo"]);
        Assert.Null(view.ViewData["Periodo"]); // Nenhum cartão dourado
        Assert.Null(view.ViewData["DataSelecionada"]); // Campo de data vazio
    }

    // =========================================================================
    // 7. 205 VENDAS = 200 LINHAS E TOTAL/CONTAGEM DAS 205
    // =========================================================================
    [Fact]
    public async Task Historico_Com205Vendas_LimitaA200LinhasMasTotalEContagemSobreTodas()
    {
        using var qa = new QaContext();
        // 205 vendas de R$ 10,00 cada hoje
        for (int i = 1; i <= 205; i++)
        {
            qa.SeedVenda(10.00m, data: Hoje.AddMinutes(i), codigoVendedor: $"V_{i}");
        }

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null, "hoje");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        // Limite de 200 linhas
        Assert.Equal(200, model.Count);

        // Total e contagem sobre todas as 205
        var contagem = Assert.IsType<int>(view.ViewData["QuantidadeVendas"]);
        var total = Assert.IsType<decimal>(view.ViewData["TotalVendas"]);
        var limiteExcedido = Assert.IsType<bool>(view.ViewData["LimiteExcedido"]);

        Assert.Equal(205, contagem);
        Assert.Equal(2050.00m, total);
        Assert.True(limiteExcedido);
        Assert.Equal("205 vendas · R$ 2.050,00", view.ViewData["Subtitulo"]);
    }

    // =========================================================================
    // 8. VAZIO: NENHUMA VENDA NO BANCO
    // =========================================================================
    [Fact]
    public async Task Historico_SemVendas_RetornaVazioSemErros()
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        var result = await controller.Historico(null, "hoje");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(view.Model).ToList();

        Assert.Empty(model);
        Assert.Equal(0, view.ViewData["QuantidadeVendas"]);
        Assert.Equal(0m, view.ViewData["TotalVendas"]);
        Assert.Equal(0m, view.ViewData["FaturamentoHoje"]);
        Assert.False((bool)view.ViewData["LimiteExcedido"]!);
        Assert.Equal("0 vendas · R$ 0,00", view.ViewData["Subtitulo"]);
    }

    // =========================================================================
    // 9. SOMENTE LEITURA: NÃO MODIFICA O BANCO
    // =========================================================================
    [Fact]
    public async Task Historico_SomenteLeitura_NaoAlteraDadosDoBanco()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10));
        qa.SeedVenda(200m, data: Ontem.AddHours(11));

        var countAntes = await qa.Context.Vendas.CountAsync();

        var controller = new CaixaController(qa.Context);
        await controller.Historico(null, "hoje");

        var countDepois = await qa.Context.Vendas.CountAsync();
        Assert.Equal(countAntes, countDepois);
    }

    // =========================================================================
    // 10. FALLBACKS: PERÍODO DESCONHECIDO, DATA FUTURA OU MODELSTATE INVÁLIDO CAEM EM HOJE
    // =========================================================================
    [Fact]
    public async Task Historico_PeriodoDesconhecidoOuDataFuturaOuModelStateInvalido_CaemEmHoje()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, data: Hoje.AddHours(10), codigoVendedor: "HOJE");
        qa.SeedVenda(50m, data: Ontem.AddHours(10), codigoVendedor: "ONTEM");

        var controller = new CaixaController(qa.Context);

        // (a) Período desconhecido cai em hoje
        var resPeriodoInvalido = await controller.Historico(null, "desconhecido");
        var view1 = Assert.IsType<ViewResult>(resPeriodoInvalido);
        var model1 = Assert.IsAssignableFrom<IEnumerable<Venda>>(view1.Model).ToList();
        Assert.Single(model1);
        Assert.Equal("HOJE", model1[0].CodigoVendedor);
        Assert.Equal("Vendas de hoje", view1.ViewData["Titulo"]);
        Assert.Equal("hoje", view1.ViewData["Periodo"]);

        // (b) Data futura cai em hoje
        var resDataFutura = await controller.Historico(Amanha, null);
        var view2 = Assert.IsType<ViewResult>(resDataFutura);
        var model2 = Assert.IsAssignableFrom<IEnumerable<Venda>>(view2.Model).ToList();
        Assert.Single(model2);
        Assert.Equal("HOJE", model2[0].CodigoVendedor);
        Assert.Equal("Vendas de hoje", view2.ViewData["Titulo"]);
        Assert.Equal("hoje", view2.ViewData["Periodo"]);

        // (c) ModelState inválido cai em hoje
        controller.ModelState.AddModelError("data", "Data inválida");
        var resModelInvalido = await controller.Historico(null, null);
        var view3 = Assert.IsType<ViewResult>(resModelInvalido);
        var model3 = Assert.IsAssignableFrom<IEnumerable<Venda>>(view3.Model).ToList();
        Assert.Single(model3);
        Assert.Equal("HOJE", model3[0].CodigoVendedor);
        Assert.Equal("Vendas de hoje", view3.ViewData["Titulo"]);
        Assert.Equal("hoje", view3.ViewData["Periodo"]);
    }
}
