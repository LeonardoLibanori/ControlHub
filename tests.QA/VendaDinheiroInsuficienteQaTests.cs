using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// BUG-29 - FinalizarVenda aceita Dinheiro com ValorRecebido &lt; TotalVenda.
///
/// POR QUE ISTO E UM BUG DE DINHEIRO E NAO DE TELA: quando o operador paga
/// 26,99 numa compra de 27,00, o codigo grava ValorRecebido = 26,99 e
/// ValorTroco = 26,99 - 27,00 = -1,01. Troco NEGATIVO persistido e um
/// registro que nenhum humano produziu: significa "o caixa deve 1,01 ao
/// cliente", que e o oposto de uma venda.
///
/// A REGRA 6 do ESTADO-DO-PROJETO ja diz: "venda em dinheiro exige
/// ValorRecebido &gt;= Total; troco recalculado no servidor". O codigo nao a
/// aplica - o comentario do proprio metodo registra QA-V01 como especificacao
/// ("mesmo com recebido &lt; total a venda e gravada"), e nao ha teste que
/// impeca a gravacao.
///
/// ESTES TESTES DEVEM FALHAR enquanto o bug existir. Nenhum teste existente
/// foi alterado.
/// </summary>
public class VendaDinheiroInsuficienteQaTests
{
    // Total de 27,00 = 3 x 9,00. Divisor de 9,00 deixa claro que o total vem
    // do item, e nao do valor recebido que mandamos.
    private const decimal PrecoUnitario = 9m;

    private static List<CaixaController.ItemVendaSimples> Item(Produto p, string qtd)
        => new()
        {
            new CaixaController.ItemVendaSimples
            {
                ProdutoId = p.Id.ToString(),
                Quantidade = qtd,
                Preco = "0", // o preco do POST e ignorado: vale o da tabela
            },
        };

    /// <summary>Prepara produto (100 un x 9,00) + vendedor e devolve o controller.</summary>
    private static (QaContext Qa, CaixaController Controller, Produto Produto) Montar()
    {
        var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(
            nome: "PRODUTO", preco: PrecoUnitario, quantidade: 100m,
            tipo: TipoProduto.Prateleira);
        return (qa, new CaixaController(qa.Context), produto);
    }

    private static bool Sucesso(IActionResult r, out string mensagem)
    {
        var json = Assert.IsType<JsonResult>(r);
        var valor = json.Value!;
        var tipo = valor.GetType();
        mensagem = (tipo.GetProperty("message")?.GetValue(valor) as string) ?? "";
        return (bool)(tipo.GetProperty("success")!.GetValue(valor)!);
    }

    // =====================================================================
    // (1) 26,99 numa compra de 27,00 e REJEITADO com mensagem clara
    // =====================================================================

    [Fact]
    public async Task FinalizarVenda_DinheiroInsuficiente_DeveRejeitarComMensagemClara()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        // 3 x 9,00 = 27,00; o operador entrega 26,99.
        var result = await controller.FinalizarVenda(
            "Dinheiro", null, "ABC123", 26.99m, 0m, Item(produto, "3"));

        var ok = Sucesso(result, out var mensagem);

        Assert.False(ok, "Dinheiro com recebido MENOR que o total tem de ser recusado.");
        Assert.False(string.IsNullOrWhiteSpace(mensagem), "A rejeicao precisa explicar o motivo.");
        // Mensagem para o OPERADOR: nada de nome de tabela, coluna ou SQLException.
        Assert.DoesNotContain("Sqlite", mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TotalVenda", mensagem, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // (2) A rejeicao nao grava NADA: nem venda, nem item, nem estoque
    // =====================================================================

    [Fact]
    public async Task FinalizarVenda_DinheiroInsuficiente_NaoGravaNada()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        var vendasAntes = qa.Context.Vendas.Count();
        var itensAntes = qa.Context.VendaItens.Count();
        var movsAntes = qa.Context.CaixaMovimentacoes.Count();
        var estoqueAntes = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id).Quantidade;

        await controller.FinalizarVenda(
            "Dinheiro", null, "ABC123", 26.99m, 0m, Item(produto, "3"));

        Assert.Equal(vendasAntes, qa.Context.Vendas.Count());
        Assert.Equal(itensAntes, qa.Context.VendaItens.Count());
        Assert.Equal(movsAntes, qa.Context.CaixaMovimentacoes.Count());

        var estoqueDepois = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id).Quantidade;
        Assert.Equal(estoqueAntes, estoqueDepois);
        Assert.Equal(100m, estoqueDepois);
    }

    // =====================================================================
    // Mais de 2 casas e valor absurdo: recusados SEM arredondar
    // =====================================================================

    [Theory]
    [InlineData(26.995)]  // arredondar para 27,00 seria aceitar dinheiro a menor
    [InlineData(27.001)]  // arredondar daria troco 0,00 em vez de 0,01
    public async Task FinalizarVenda_DinheiroComMaisDe2Casas_DeveRejeitarSemGravar(decimal recebido)
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        IActionResult result;
        try
        {
            result = await controller.FinalizarVenda(
                "Dinheiro", null, "ABC123", recebido, 0m, Item(produto, "3"));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Valor com 3 casas nao pode derrubar com 500: {ex.Message}");
            return;
        }

        var ok = Sucesso(result, out var mensagem);
        Assert.False(ok, $"{recebido} tem 3 casas: nao arredonda, recusa.");
        Assert.False(string.IsNullOrWhiteSpace(mensagem));
        Assert.DoesNotContain("Exception", mensagem, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
        Assert.Equal(100m, qa.Context.Produtos.AsNoTracking()
            .First(p => p.Id == produto.Id).Quantidade);
    }

    [Fact]
    public async Task FinalizarVenda_DinheiroAcimaDoMaximoArmazenavel_RecusaSem500()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        // Acima de ConversorCentavos.MaxCentavos: hoje estoura o INTEGER do
        // SQLite no SaveChanges (500). A recusa tem de vir antes.
        var absurdo = (decimal)ConversorCentavos.MaxCentavos / 100m + 1000m;

        IActionResult result;
        try
        {
            result = await controller.FinalizarVenda(
                "Dinheiro", null, "ABC123", absurdo, 0m, Item(produto, "3"));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Valor acima do maximo nao pode derrubar com 500: {ex.Message}");
            return;
        }

        Assert.False(Sucesso(result, out _));
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
        Assert.Equal(100m, qa.Context.Produtos.AsNoTracking()
            .First(p => p.Id == produto.Id).Quantidade);
    }

    // =====================================================================
    // (3) Borda: exatamente o total e um centavo acima - aceitos
    // =====================================================================

    [Theory]
    [InlineData(27.00, 0.00)]   // paga exato
    [InlineData(27.01, 0.01)]   // sobra 1 centavo
    public async Task FinalizarVenda_DinheiroSuficiente_DeveAceitar(decimal recebido, decimal trocoEsperado)
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        var result = await controller.FinalizarVenda(
            "Dinheiro", null, "ABC123", recebido, 999m, Item(produto, "3"));

        var ok = Sucesso(result, out var mensagem);
        Assert.True(ok, $"Recebido {recebido} >= total 27,00 deveria passar: {mensagem}");

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(27.00m, venda.TotalVenda);
        Assert.Equal(trocoEsperado, venda.ValorTroco);
        Assert.Equal(97m, qa.Context.Produtos.AsNoTracking()
            .First(p => p.Id == produto.Id).Quantidade);
    }

    // =====================================================================
    // (4) Guarda do comportamento atual: dinheiro alto gera troco grande
    // =====================================================================

    [Fact]
    public async Task FinalizarVenda_DinheiroAcimaDoTotal_CalculaTrocoDe23Reais()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        var result = await controller.FinalizarVenda(
            "Dinheiro", null, "ABC123", 50.00m, 0m, Item(produto, "3"));

        Assert.True(Sucesso(result, out _));

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(27.00m, venda.TotalVenda);
        Assert.Equal(50.00m, venda.ValorRecebido);
        Assert.Equal(23.00m, venda.ValorTroco);
    }

    // =====================================================================
    // (5) Recebido ausente, zero ou negativo: recusado SEM 500 e sem gravar
    // =====================================================================

    [Theory]
    [InlineData(0)]      // o campo em branco vira 0 no binding
    [InlineData(-10)]    // negativo
    public async Task FinalizarVenda_DinheiroZeroOuNegativo_DeveRejeitarSemGravar(decimal recebido)
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        IActionResult result;
        try
        {
            result = await controller.FinalizarVenda(
                "Dinheiro", null, "ABC123", recebido, 0m, Item(produto, "3"));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Dinheiro com recebido {recebido} nao pode derrubar com 500: {ex.Message}");
            return;
        }

        var ok = Sucesso(result, out _);
        Assert.False(ok);
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
        Assert.Equal(100m, qa.Context.Produtos.AsNoTracking()
            .First(p => p.Id == produto.Id).Quantidade);
    }

    [Fact]
    public async Task FinalizarVenda_DinheiroNoTetoExatoDoConversor_EEAceito()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        // CONTROLE do limite: 9 trilhoes e ENORME, mas cabe no long de centavos
        // e tem 2 casas, entao e um valor legitimo - a venda tem de passar. Impede
        // que a faixa de recusa fique broad demais por engano (o teto vem de
        // ConversorCentavos.MaxCentavos, nao de um numero escolhido a mao).
        var dentroDoTeto = (decimal)ConversorCentavos.MaxCentavos / 100m;

        var result = await controller.FinalizarVenda(
            "Dinheiro", null, "ABC123", dentroDoTeto, 0m, Item(produto, "3"));

        Assert.True(Sucesso(result, out _));
        Assert.Single(qa.Context.Vendas.AsNoTracking().ToList());
    }

    // =====================================================================
    // (6) Pix / Debito / Credito nao exigem valor recebido
    // =====================================================================

    [Theory]
    [InlineData("Pix")]
    [InlineData("Debito")]
    [InlineData("Credito")]
    public async Task FinalizarVenda_Eletronico_NaoExigeValorRecebido(string metodo)
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        // Recebido 0: no eletronico o servidor deve gravar o proprio total.
        var result = await controller.FinalizarVenda(
            metodo, null, "ABC123", 0m, 0m, Item(produto, "3"));

        var ok = Sucesso(result, out var mensagem);
        Assert.True(ok, $"{metodo} nao exige valor recebido: {mensagem}");

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(27.00m, venda.TotalVenda);
        Assert.Equal(27.00m, venda.ValorRecebido);
        Assert.Equal(0m, venda.ValorTroco);
    }

    // =====================================================================
    // (7) "dinheiro" em minusculas tambem e validado
    // =====================================================================

    [Fact]
    public async Task FinalizarVenda_DinheiroMinusculo_ValidacaoCaseInsensitive()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        // O PDV manda "Dinheiro" com D maiusculo, mas o valor pode chegar de
        // outro lugar. A comparacao JAH e OrdinalIgnoreCase (L190): a regra nova
        // precisa herdar isso, senao "dinheiro" passaria sem validacao.
        var insuficiente = await controller.FinalizarVenda(
            "dinheiro", null, "ABC123", 26.99m, 0m, Item(produto, "3"));
        Assert.False(Sucesso(insuficiente, out _), "'dinheiro' minusculo tambem tem de ser validado.");
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());

        // E o suficiente tambem passa com caixa baixa.
        var suficiente = await controller.FinalizarVenda(
            "dinheiro", null, "ABC123", 30.00m, 0m, Item(produto, "3"));
        Assert.True(Sucesso(suficiente, out _));
        Assert.Equal(3.00m, qa.Context.Vendas.AsNoTracking().Single().ValorTroco);
    }

    // =====================================================================
    // (8) Venda com VARIOS itens e recebido insuficiente: nenhum baixa estoque
    // =====================================================================

    [Fact]
    public async Task FinalizarVenda_VariosItens_Insuficiente_NaoBaixaNenhumEstoque()
    {
        var (qa, controller, produto) = Montar();
        using var vida = qa;

        var outro = qa.SeedProduto(
            nome: "OUTRO", preco: 9m, quantidade: 50m, tipo: TipoProduto.Prateleira);

        var itens = new List<CaixaController.ItemVendaSimples>();
        foreach (var p in new[] { produto, outro })
        {
            itens.Add(new CaixaController.ItemVendaSimples
            {
                ProdutoId = p.Id.ToString(),
                Quantidade = "2",
                Preco = "0",
            });
        }
        // Total = 2 x 9,00 + 2 x 9,00 = 36,00. Recebido 35,00 => insuficiente.

        var result = await controller.FinalizarVenda(
            "Dinheiro", null, "ABC123", 35.00m, 0m, itens);

        Assert.False(Sucesso(result, out _));

        // NENHUM dos dois produtos pode ter baixado: a rejeicao tem de vir
        // antes de qualquer baixa, nao no meio do laco.
        Assert.Equal(100m, qa.Context.Produtos.AsNoTracking()
            .First(p => p.Id == produto.Id).Quantidade);
        Assert.Equal(50m, qa.Context.Produtos.AsNoTracking()
            .First(p => p.Id == outro.Id).Quantidade);
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
        Assert.Empty(qa.Context.VendaItens.AsNoTracking().ToList());
    }
}