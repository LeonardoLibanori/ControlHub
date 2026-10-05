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
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
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
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
            Item(produto.Id.ToString(), qtd));

        Assert.False(result.IsSuccess());
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
    }

    // BUG-03: preço em R$/kg multiplicado diretamente pela quantidade fracionária em kg.
    [Fact]
    public async Task FinalizarVenda_UsaPrecoDiretoPorQuilo()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        // R$ 50,00/kg
        var porPeso = qa.SeedProduto(preco: 50m, quantidade: 50m, tipo: TipoProduto.PorPeso);

        var controller = new CaixaController(qa.Context);
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
            Item(porPeso.Id.ToString(), "2"));

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(100m, venda.TotalVenda);   // 50,00/kg x 2 kg
        Assert.Equal(50m, qa.Context.VendaItens.AsNoTracking().Single().PrecoUnitario);
    }

    [Fact]
    public async Task FinalizarVenda_UsaPrecoDiretoComQuantidadeFracionariaEmQuilos()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var porPeso = qa.SeedProduto(preco: 50m, quantidade: 10m, tipo: TipoProduto.PorPeso);

        var controller = new CaixaController(qa.Context);
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
            Item(porPeso.Id.ToString(), "0,125"));

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(6.25m, venda.TotalVenda);
    }

    // JA CORRIGIDO: produto inexistente invalida a venda (sem venda vazia).
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarProdutoInexistente()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
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
        var result = await controller.FinalizarVenda("Dinheiro", null, "NAOEXISTE", 100m, 0m,
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
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
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
        var result = await controller.FinalizarVenda("", null, "ABC123", 100m, 0m,
            Item(produto.Id.ToString(), "1"));

        Assert.False(result.IsSuccess());
    }

    // Produto vendido por unidade continua aceitando apenas quantidades inteiras.
    [Fact]
    public async Task FinalizarVenda_DeveRejeitarQuantidadeFracionadaEmUnitario()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(quantidade: 10m, tipo: TipoProduto.Prateleira);

        var controller = new CaixaController(qa.Context);
        var result = await controller.FinalizarVenda("Dinheiro", null, "ABC123", 100m, 0m,
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
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 30m, 0m,
            Item(produto.Id.ToString(), "3"));

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);
        Assert.Equal(7m, salvo.Quantidade);
    }

    // JA CORRIGIDO (Fase 1): o troco e recalculado no servidor
    // (recebido - total). O valorTroco do POST e ignorado: o vendedor nao pode
    // informar quanto devolver, senao daria para "devolver" menos que o devido.
    // Este nome e o que qa/falhas.csv (FIX-04) promete como evidencia.
    [Fact]
    public async Task FinalizarVenda_DeveCalcularTrocoNoServidor()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(preco: 10m, quantidade: 100m, tipo: TipoProduto.Prateleira);

        var controller = new CaixaController(qa.Context);
        // 2 un x 10,00 = 20,00; recebido 50,00 -> troco 30,00.
        // O POST mente no troco (999,00) e o servidor precisa ignorar.
        await controller.FinalizarVenda("Dinheiro", null, "ABC123", 50m, 999m,
            Item(produto.Id.ToString(), "2"));

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(20m, venda.TotalVenda);
        Assert.Equal(50m, venda.ValorRecebido);
        Assert.Equal(30m, venda.ValorTroco);
    }

    // Pagamento eletronico nao devolve troco e o recebido vira o proprio total,
    // senao a gaveta (que soma apenas vendas em Dinheiro) inflaria pelo POST.
    [Fact]
    public async Task FinalizarVenda_DeveZerarTrocoEmPagamentoEletronico()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(codigo: "ABC123");
        var produto = qa.SeedProduto(preco: 10m, quantidade: 100m, tipo: TipoProduto.Prateleira);

        var controller = new CaixaController(qa.Context);
        await controller.FinalizarVenda("Pix", null, "ABC123", 999m, 999m,
            Item(produto.Id.ToString(), "2"));

        var venda = qa.Context.Vendas.AsNoTracking().Single();
        Assert.Equal(20m, venda.TotalVenda);
        Assert.Equal(20m, venda.ValorRecebido);
        Assert.Equal(0m, venda.ValorTroco);
    }
}
