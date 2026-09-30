using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Produtos (HomeController).
/// Devem FALHAR enquanto o bug existir e PASSAR quando corrigido.
/// </summary>
public class ProdutosQaTests
{
    // BUG: "Editar produto zera o estoque".
    // HomeController.Editar(int id, ..., string quantidade) faz
    // decimal.TryParse(quantidade, ...) sem verificar o retorno; a view
    // Views/Home/Editar.cshtml NAO envia o campo quantidade, entao qtd = 0 e
    // produto.Quantidade = 0 -> perda silenciosa de estoque.
    [Fact]
    public void Editar_DevePreservarEstoque_QuandoCampoQuantidadeNaoEnviado()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "REFRIGERANTE", preco: 5m, quantidade: 42m);

        var controller = new HomeController(qa.Context).ComTempData();

        // A view de edicao envia nome/tipo/preco, mas NAO envia quantidade.
        controller.Editar(produto.Id, "REFRIGERANTE", "Prateleira", quantidade: null, preco: "700");

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);
        Assert.Equal(42m, salvo.Quantidade);
    }

    // BUG: o cadastro aceita preco NEGATIVO.
    // HomeController.Novo divide o valor por 100 sem validar sinal:
    // preco "-500" -> precoFinal = -5.00 e o produto e gravado.
    [Theory]
    [InlineData("-500")]   // -R$ 5,00
    [InlineData("-1")]     // -R$ 0,01
    public void Novo_DeveRejeitarPrecoNegativo(string precoEnviado)
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("PRODUTO CARO", "Prateleira", "10", precoEnviado, "111");

        var cadastrado = qa.Context.Produtos.AsNoTracking()
            .FirstOrDefault(p => p.CodigoBarras == "111");

        Assert.True(cadastrado is null || cadastrado.Preco >= 0m,
            $"Cadastro aceitou preco negativo: {cadastrado?.Preco}. Deve ser rejeitado.");
    }

    // BUG: o cadastro aceita estoque NEGATIVO.
    // HomeController.Novo usa decimal.TryParse(quantidade) sem validar sinal.
    [Fact]
    public void Novo_DeveRejeitarEstoqueNegativo()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("PRODUTO FANTASMA", "Prateleira", "-10", "500", "222");

        var cadastrado = qa.Context.Produtos.AsNoTracking()
            .FirstOrDefault(p => p.CodigoBarras == "222");

        Assert.True(cadastrado is null || cadastrado.Quantidade >= 0m,
            $"Cadastro aceitou estoque negativo: {cadastrado?.Quantidade}. Deve ser rejeitado.");
    }

    // BUG: o cadastro aceita codigo de barras DUPLICADO.
    // HomeController.Novo nao verifica se CodigoBarras ja existe, ao contrario de
    // CadastrarVendedor que faz a checagem. Dois produtos ficam com o mesmo codigo e
    // BuscarPorCodigo devolve sempre o primeiro (FirstOrDefault), quebrando o PDV.
    [Fact]
    public void Novo_DeveRejeitarCodigoDeBarrasDuplicado()
    {
        using var qa = new QaContext();
        qa.SeedProduto(nome: "PRIMEIRO", codigoBarras: "7891000000001");
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("SEGUNDO", "Prateleira", "10", "500", "7891000000001");

        var comMesmoCodigo = qa.Context.Produtos.AsNoTracking()
            .Count(p => p.CodigoBarras == "7891000000001");

        Assert.Equal(1, comMesmoCodigo);
    }

    // BUG: ON DELETE CASCADE em VendaItens.ProdutoId.
    // A migration Inicial_Nova define FK_VendaItens_Produtos_ProdutoId com
    // onDelete: ReferentialAction.Cascade. Ao excluir um produto, o EF apaga o produto
    // e o banco apaga em cascata TODOS os VendaItens daquele produto -> o
    // historico de vendas (e o relatorio financeiro) perde registros.
    [Fact]
    public void Excluir_DevePreservarHistoricoDeVendas_DoProduto()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "PRODUTO VENDIDO", quantidade: 10m);

        var venda = qa.SeedVenda(50m, codigoVendedor: "ABC123");
        qa.Context.VendaItens.Add(new VendaItem
        {
            ProdutoId = produto.Id,
            VendaId = venda.Id,
            Quantidade = 2m,
            PrecoUnitario = 25m
        });
        qa.Context.SaveChanges();

        var controller = new HomeController(qa.Context).ComTempData();
        controller.Excluir(produto.Id);

        var itensSobrevivos = qa.Context.VendaItens.AsNoTracking()
            .Count(i => i.VendaId == venda.Id);

        Assert.True(itensSobrevivos > 0,
            "Excluir o produto apagou o historico de vendas (ON DELETE CASCADE). " +
            "O historico financeiro deve ser preservado.");
    }

    // CONTROLE (deve PASSAR hoje): cadastro valido funciona normalmente.
    [Fact]
    public void Novo_DeveCadastrarProdutoValido()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("LEITE INTEGRAL", "Prateleira", "12", "500", "7891000000009");

        var cadastrado = qa.Context.Produtos.AsNoTracking()
            .FirstOrDefault(p => p.CodigoBarras == "7891000000009");

        Assert.NotNull(cadastrado);
        Assert.Equal(5m, cadastrado!.Preco);   // 500 / 100
        Assert.Equal(12m, cadastrado.Quantidade);
    }

    // CONTROLE (deve PASSAR hoje): AjustarEstoque remove ate o saldo e barra o excedente.
    [Fact]
    public async Task AjustarEstoque_DeveBarrarRemocaoMaiorQueSaldo()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(quantidade: 5m);
        var controller = new HomeController(qa.Context).ComTempData();

        await controller.AjustarEstoque(produto.Id, 10m, "remover");

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);
        Assert.Equal(5m, salvo.Quantidade);
    }
}
