using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Vendas (CaixaController.FinalizarVenda).
/// Cobre a Fase 1 ja corrigida (preco de tabela, quantidade validada, troco no servidor)
/// e os limites que continuam em aberto.
/// </summary>
public class VendasQaTests
{
    private static List<CaixaController.ItemVendaSimples> Item(string produtoId, string qtd, string preco = "0")
        => new() { new CaixaController.ItemVendaSimples { ProdutoId = produtoId, Quantidade = qtd, Preco = preco } };

    // JA CORRIGIDO (Fase 1): o preco vem SEMPRE da tabela (produto.Preco).
    // O campo item.Preco do POST e ignorado -> impede fraude via POST manual.
    [Fact]
    public async Task FinalizarVenda_DeveIgnorarPrecoAdulteradoNoPost()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(preco: 10m, quantidade: 100m, tipo: TipoProduto.Prateleira);

        var controller = new CaixaController(qa.Context);

        // Vendedor tenta mandar preco = 0,01 no POST.
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(produto.Id.ToString(), "2", preco: "0,01"));

        var venda = qa.Context.Vendas.AsNoTracking()
            .Include(v => v.Itens)
            .Single();
        Assert.Equal(20m, venda.TotalVenda);          // 10,00 x 2
        Assert.Equal(10m, venda.Itens.Single().PrecoUnitario);
    }

    // JA CORRIGIDO (Fase 1): quantidade <= 0 e rejeitada
    // (quantidade negativa somaria estoque; zero geraria lixo no historico).
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0,5")]
    public async Task FinalizarVenda_DeveRejeitarQuantidadeNaoPositiva(string qtd)
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(quantidade: 10m);

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(produto.Id.ToString(), qtd));

        Assert.False(result.IsSuccess());
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
    }

    // UNIDADES GENERALIZADAS (substitui o antigo "multiplicador x10 em Granel").
    // O teste anterior media uma regra presa ao valor "Granel" de um enum de LOCAL DE
    // GUARDA: o cadastro guardava o preco por 100 g, a quantidade entrava em kg e o
    // caixa refazia a conta x10. A conta batia para um unico case e mentia para
    // qualquer outra medida (litro, caixa, pacote). A regra agora e una e o que se
    // cobra aqui e ela: subtotal = preco de UMA unidade x quantidade.
    [Fact]
    public async Task FinalizarVenda_DeveMultiplicarPrecoDaUnidadePelaQuantidade()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        // R$ 50,00 o quilo - o que antes era gravado como "R$ 5,00 por 100 g" + Granel.
        var queijo = qa.SeedProduto(nome: "QUEIJO MUSSARELA", preco: 50m, quantidade: 50m,
                                    unidade: "kg", permiteFracionado: true);

        var controller = new CaixaController(qa.Context);
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(queijo.Id.ToString(), "2"));   // 2 kg

        var venda = qa.Context.Vendas.AsNoTracking().Include(v => v.Itens).Single();
        Assert.Equal(100m, venda.TotalVenda);                 // 50,00 x 2
        Assert.Equal(50m, venda.Itens.Single().PrecoUnitario); // preco de 1 kg, nao de 100 g
    }

    // UNIDADES GENERALIZADAS (2): a mesma regra vale para outra medida fracionavel.
    // Se alguem voltar a amarrar "fracionavel" a "kg"/a um valor de enum, este teste pega.
    [Fact]
    public async Task FinalizarVenda_DeveAceitarFracaoEmQualquerUnidadeFracionavel()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var refrigerante = qa.SeedProduto(nome: "REFRIGERANTE DE 2 L", preco: 8m, quantidade: 30m,
                                          tipo: TipoProduto.Geladeira, unidade: "l",
                                          permiteFracionado: true, codigoBarras: "7891000000002");

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(refrigerante.Id.ToString(), "1,5"));   // 1,5 l

        // Nao usa result.GetStr("message") aqui: a resposta de sucesso nao tem a
        // propriedade "message" e o helper arrebentaria antes da afirmacao de verdade.
        Assert.True(result.IsSuccess());
        var venda = qa.Context.Vendas.AsNoTracking().Include(v => v.Itens).Single();
        Assert.Equal(12m, venda.TotalVenda);   // 8,00 x 1,5
        Assert.Equal(8m, venda.Itens.Single().PrecoUnitario);

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == refrigerante.Id);
        Assert.Equal(28.5m, salvo.Quantidade);
    }

    // JA CORRIGIDO: produto inexistente invalida a venda (sem venda vazia).
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarProdutoInexistente()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item("999999", "1"));

        Assert.False(result.IsSuccess());
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
    }

    // JA CORRIGIDO: vendedor inexistente/inativo invalida a venda.
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarVendedorInexistente()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(quantidade: 10m);

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "NAOEXISTE", 0m, 0m,
            Item(produto.Id.ToString(), "1"));

        Assert.False(result.IsSuccess());
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
    }

    // JA CORRIGIDO: estoque insuficiente e barrado com mensagem ao vendedor.
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarQuantidadeAcimaDoEstoque()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(quantidade: 3m);

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(produto.Id.ToString(), "5"));

        Assert.False(result.IsSuccess());
        Assert.Contains("Estoque insuficiente", result.GetStr("message"));
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
    }

    // JA CORRIGIDO: metodo de pagamento ausente nao causa HTTP 500.
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarMetodoPagamentoVazio()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(quantidade: 10m);

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("", null, "ABC123", 0m, 0m,
            Item(produto.Id.ToString(), "1"));

        Assert.False(result.IsSuccess());
    }

    // FASE 1 (regra agora no cadastro): quantidade fracionada so e aceita quando o
    // produto declara PermiteFracionado. O produto deste teste nasceu com a unidade
    // inteira padrao ("un"), entao 1,5 tem de ser barrado - e nao porque o "tipo" dele
    // se chama Prateleira, como decidia o codigo antigo.
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarQuantidadeFracionadaEmUnitario()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(quantidade: 10m, tipo: TipoProduto.Prateleira);

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(produto.Id.ToString(), "1,5"));

        Assert.False(result.IsSuccess());
    }

    // CONTROLE: a baixa de estoque deve acontecer na venda.
    [Fact]
    public async Task FinalizarVenda_DeveBaixarEstoqueDoProduto()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(quantidade: 10m);

        var controller = new CaixaController(qa.Context);
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 0m, 0m,
            Item(produto.Id.ToString(), "3"));

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);
        Assert.Equal(7m, salvo.Quantidade);
    }
}
