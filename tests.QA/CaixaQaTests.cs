using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Caixa (CaixaController: fechamento, status e movimentacoes).
/// Devem FALHAR enquanto o bug existir e PASSAR quando corrigido.
/// </summary>
public class CaixaQaTests
{
    // ---------------------------------------------------------------------
    // BUG 1 (sinal da sangria) - ObterDadosFechamento
    // RegistrarMovimentacao grava a sangria JA NEGATIVA (-Math.Abs(valor)).
    // ObterDadosFechamento faz: saldoFinal = (totalVendas + suprimentos) - sangrias
    // Como sangrias ja e negativo, a subtracao ANULA o sinal e a sangria
    // SOMA ao saldo em vez de subtrair.
    // Ex.: venda 100 + suprimento 0 + sangria 50 (gravada -50)
    //      esperado 50 | obtido (100 + 0) - (-50) = 150  <-- ERRADO
    // ---------------------------------------------------------------------
    [Fact]
    public void ObterDadosFechamento_DeveSubtrairSangriaDoSaldo()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");
        qa.SeedMovimentacao("Sangria", -50m);   // sangria de R$ 50,00 ja negativa

        var controller = new CaixaController(qa.Context);
        var result = controller.ObterDadosFechamento();

        var saldoFinal = (decimal)result.GetProp("saldoFinal")!;
        Assert.Equal(50m, saldoFinal);   // 100 - 50
    }

    // ---------------------------------------------------------------------
    // BUG 1 (mesmo bug, visivel em ObterStatusAtual)
    // ObterStatusAtual soma: totalVendas + suprimentos + sangrias, coerente
    // com a sangria negativa. Este teste documenta o COMPORTAMENTO CORRETO
    // esperado e deve falhar enquanto o bug do fechamento existir.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ObterStatusAtual_DeveSubtrairSangriaDoSaldoEmGaveta()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");
        qa.SeedMovimentacao("Sangria", -50m);

        var controller = new CaixaController(qa.Context);
        var result = await controller.ObterStatusAtual();

        var saldo = result.GetNum("saldo");
        Assert.Equal(50d, saldo, 2);
    }

    // ---------------------------------------------------------------------
    // COMPLEMENTO A (escopo do saldo em gaveta)
    // O saldo da gaveta e FISICO: so dinheiro entra nele. Vendas em Credito/
    // Debito/PIX nao deixam dinheiro no caixa, mas hoje o saldo as inclui.
    // ---------------------------------------------------------------------
    [Fact]
    public void ObterDadosFechamento_SaldoDeveConsiderarSomenteDinheiro()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");
        qa.SeedVenda(500m, metodoPagamento: "Credito");   // nao entra na gaveta

        var controller = new CaixaController(qa.Context);
        var result = controller.ObterDadosFechamento();

        var saldoFinal = (decimal)result.GetProp("saldoFinal")!;
        Assert.Equal(100m, saldoFinal);   // apenas o dinheiro
    }

    [Fact]
    public async Task ObterStatusAtual_SaldoDeveConsiderarSomenteDinheiro()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");
        qa.SeedVenda(500m, metodoPagamento: "Debito");

        var controller = new CaixaController(qa.Context);
        var result = await controller.ObterStatusAtual();

        Assert.Equal(100d, result.GetNum("saldo"), 2);
    }

    [Fact]
    public async Task RelatorioDados_SaldoFinalDeveConsiderarSomenteDinheiro()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");
        qa.SeedVenda(500m, metodoPagamento: "Pix");

        var controller = new CaixaController(qa.Context);
        var result = await controller.RelatorioDados();

        Assert.Equal(100d, result.GetNum("saldoFinal"), 2);
    }

    // ---------------------------------------------------------------------
    // BUG 2 (RegistrarMovimentacao aceita sangria maior que o saldo)
    // Nao existe validacao de saldo: o caixa pode retirar mais dinheiro
    // do que ha em gaveta, leaving o saldo negativo sem aviso.
    // ---------------------------------------------------------------------
    [Fact]
    public void RegistrarMovimentacao_DeveRejeitarSangriaMaiorQueOSaldo()
    {
        using var qa = new QaContext();
        // Gaveta com apenas R$ 20,00 de vendas em dinheiro.
        qa.SeedVenda(20m, metodoPagamento: "Dinheiro");

        var controller = new CaixaController(qa.Context);
        var result = controller.RegistrarMovimentacao("Sangria", "500", "tentativa indevida");

        Assert.False(result.IsSuccess());
        Assert.Empty(qa.Context.CaixaMovimentacoes.AsNoTracking().ToList());
    }

    // ---------------------------------------------------------------------
    // BUG 2b (RegistrarMovimentacao aceita valor <= 0)
    // decimal.Parse sem validar sinal + Math.Abs: um valor 0 ou negativo
    // gera movimentacao fantasma (Sangria 0 / Suprimento 0) e polui o relatorio.
    // ---------------------------------------------------------------------
    [Theory]
    [InlineData("0")]
    [InlineData("-50")]
    public void RegistrarMovimentacao_DeveRejeitarValorNaoPositivo(string valor)
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        var result = controller.RegistrarMovimentacao("Suprimento", valor, "valor invalido");

        Assert.False(result.IsSuccess());
        Assert.Empty(qa.Context.CaixaMovimentacoes.AsNoTracking().ToList());
    }

    // CONTROLE: suprimento valido deve ser aceito e gravado positivo.
    [Fact]
    public void RegistrarMovimentacao_DeveAceitarSuprimentoValido()
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        var result = controller.RegistrarMovimentacao("Suprimento", "100", "abertura de caixa");

        Assert.True(result.IsSuccess());
        var mov = qa.Context.CaixaMovimentacoes.AsNoTracking().Single();
        Assert.Equal(100m, mov.Valor);
        Assert.Equal("Suprimento", mov.Tipo);
    }

    // CONTROLE: a sangria valida e gravada com sinal negativo.
    [Fact]
    public void RegistrarMovimentacao_DeveGravarSangriaComSinalNegativo()
    {
        using var qa = new QaContext();
        qa.SeedVenda(1000m, metodoPagamento: "Dinheiro");

        var controller = new CaixaController(qa.Context);
        var result = controller.RegistrarMovimentacao("Sangria", "50", "sangria valida");

        Assert.True(result.IsSuccess());
        var mov = qa.Context.CaixaMovimentacoes.AsNoTracking().Single();
        Assert.Equal(-50m, mov.Valor);
    }

    // CONTROLE: o fechamento do caixa e idempotente - chamar duas vezes
    // nao pode duplicar nem alterar o resultado ja consolidado.
    [Fact]
    public void Fechamento_DeveSerIdempotente()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");

        var controller = new CaixaController(qa.Context);
        controller.ZerarCaixaParaProximoDia();

        var depois = controller.ObterDadosFechamento();
        var saldoAposPrimeiroFechamento = depois.GetProp("saldoFinal");

        // Segunda chamada nao deve alterar nada.
        controller.ZerarCaixaParaProximoDia();
        var final = controller.ObterDadosFechamento();

        Assert.Equal(saldoAposPrimeiroFechamento, final.GetProp("saldoFinal"));
    }

    // CONTROLE: apos o fechamento, o saldo do dia deve ser zerado.
    [Fact]
    public void Fechamento_DeveZerarSaldoDoDia()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro");

        var controller = new CaixaController(qa.Context);
        controller.ZerarCaixaParaProximoDia();

        var result = controller.ObterDadosFechamento();
        Assert.Equal(0m, result.GetProp("saldoFinal"));
    }
}
