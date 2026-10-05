using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// BUG-28 - ObterStatusAtual nao filtra IsFechado.
///
/// QUANDO ISSO ACONTECE: o Dono fecha o caixa do dia (ZerarCaixaParaProximoDia).
/// A acao marca Vendas e CaixaMovimentacoes de hoje com IsFechado = true.
/// A gaveta certa e a do dia que AINDA esta aberto: o dinheiro de ontem foi da
/// gaveta de ontem, e o de hoje so conta ate o fechamento.
///
/// O QUE O CODIGO FAZ: ObterStatusAtual (L321, L325, L331) soma as vendas, os
/// suprimentos e as sangrias de hoje SEM o filtro !IsFechado, enquanto
/// RegistrarMovimentacaoInterno (regra 7, L263/L268/L273) e
/// ObterDadosFechamento (L756/L764/L768) filtram. Resultado: o botao CAIXA do
/// PDV ("CONFERENCIA DE CAIXA", admin-caixa.js L113) mostra um saldo MAIOR que
/// o real, logo apos o fechamento.
///
/// ESTES TESTES DEVEM FALHAR enquanto o bug existir. Nenhum teste existente foi
/// alterado.
/// </summary>
public class StatusCaixaFechadoQaTests
{
    private static DateTime Hoje => DateTime.Today;

    private static decimal Saldo(JsonResult json)
    {
        var valor = json.Value!;
        return Convert.ToDecimal(valor.GetType()
            .GetProperty("saldo")!.GetValue(valor));
    }

    private static bool Sucesso(JsonResult json)
    {
        var valor = json.Value!;
        return (bool)valor.GetType().GetProperty("success")!.GetValue(valor)!;
    }

    private static string Mensagem(JsonResult json)
    {
        var valor = json.Value!;
        return valor.GetType().GetProperty("message")?.GetValue(valor) as string ?? "";
    }

    // =====================================================================
    // (9) Venda Dinheiro com IsFechado = true NAO entra no saldo
    // =====================================================================

    [Fact]
    public async Task ObterStatusAtual_IgnoraVendaDinheiroFechada()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(500m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10),
            isFechado: true);

        var controller = new CaixaController(qa.Context);
        var json = (JsonResult)await controller.ObterStatusAtual();

        Assert.Equal(100m, Saldo(json));
    }

    // =====================================================================
    // (10) Suprimento e Sangria fechados NAO entram
    // =====================================================================

    [Fact]
    public async Task ObterStatusAtual_IgnoraSuprimentoESangriaFechados()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));

        // Um suprimento e uma sangria ABERTOS (contam) e os mesmos fechados.
        var supAberto = qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        var sangriaAberta = qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(11));
        var supFechado = qa.SeedMovimentacao("Suprimento", 30m, data: Hoje.AddHours(8));
        var sangriaFechada = qa.SeedMovimentacao("Sangria", -40m, data: Hoje.AddHours(12));

        supFechado.IsFechado = true;
        sangriaFechada.IsFechado = true;
        qa.Context.SaveChanges();

        var controller = new CaixaController(qa.Context);
        var json = (JsonResult)await controller.ObterStatusAtual();

        // Correto: 100 (venda) + 50 (sup) - 20 (sangria) = 130.
        Assert.Equal(130m, Saldo(json));
        Assert.NotNull(supAberto);
        Assert.NotNull(sangriaAberta);
    }

    // =====================================================================
    // (11) Dias anteriores nao entram (o filtro e "hoje", nao acumulado)
    // =====================================================================

    [Fact]
    public async Task ObterStatusAtual_IgnoraDiasAnteriores()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(300m, metodoPagamento: "Dinheiro", data: Hoje.AddDays(-1).AddHours(9));
        qa.SeedMovimentacao("Suprimento", 40m, data: Hoje.AddDays(-2).AddHours(9));

        var controller = new CaixaController(qa.Context);
        var json = (JsonResult)await controller.ObterStatusAtual();

        // De ontem e anteontem nao entram: so o dia de hoje conta.
        Assert.Equal(100m, Saldo(json));
    }

    // =====================================================================
    // (12) Depois de ZerarCaixaParaProximoDia o saldo e 0
    // =====================================================================

    [Fact]
    public async Task ObterStatusAtual_ZeroDepoisDeFecharODia()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(250m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10));
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(11));

        var controller = new CaixaController(qa.Context);
        var antes = Saldo((JsonResult)await controller.ObterStatusAtual());
        Assert.Equal(380m, antes);   // 100 + 250 + 50 - 20

        controller.ZerarCaixaParaProximoDia();

        var depois = Saldo((JsonResult)await controller.ObterStatusAtual());
        Assert.Equal(0m, depois);
    }

    // =====================================================================
    // (13) O saldo bate com o limite que a REGRA 7 aceita na sangria
    // =====================================================================

    [Fact]
    public async Task ObterStatusAtual_SaldoEhOMesmoLimiteDaSangria()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(9));
        qa.SeedVenda(500m, metodoPagamento: "Dinheiro", data: Hoje.AddHours(10),
            isFechado: true);   // nao pode contar
        qa.SeedMovimentacao("Suprimento", 50m, data: Hoje.AddHours(8));
        qa.SeedMovimentacao("Sangria", -20m, data: Hoje.AddHours(11));
        qa.SeedMovimentacao("Suprimento", 30m, data: Hoje.AddHours(8)); // sera fechado

        var movFechado = qa.Context.CaixaMovimentacoes
            .Where(m => m.Tipo == "Suprimento" && m.Valor == 30m).Single();
        movFechado.IsFechado = true;
        qa.Context.SaveChanges();

        var controller = new CaixaController(qa.Context);

        // O botao CAIXA diz quanto tem na gaveta...
        var json = (JsonResult)await controller.ObterStatusAtual();
        var saldo = Saldo(json);

        // ...e a sangria respeita ESSE mesmo numero: 100 + 50 - 20 = 130.
        Assert.Equal(130m, saldo);

        // No limite: aceita (o valor em centavos, sem ponto).
        var noLimite = controller.RegistrarSangria("130.00", "saque no limite");
        Assert.True(Sucesso((JsonResult)noLimite),
            $"Sangria de 130,00 (= saldo {saldo:N2}) deveria ser aceita: {Mensagem((JsonResult)noLimite)}");

        // Um centavo acima: recusado. Seria o troco do BUG-29 se o PDV deixasse.
        var acima = (JsonResult)controller.RegistrarSangria("130.01", "um centavo acima");
        Assert.False(Sucesso(acima),
            "Sangria de 130,01 com saldo de 130,00 tem de ser recusada pela regra 7.");
    }
}