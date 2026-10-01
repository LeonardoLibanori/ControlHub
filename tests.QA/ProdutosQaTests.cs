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

    // =====================================================================
    // ESTOQUE MINIMO (item 1 da Fase A): cadastro + regra de alerta.
    // A comparacao e feita em C# justamente porque decimal cai em TEXT no
    // SQLite: em SQL a collation BINARY trataria "9" como maior que "10".
    // =====================================================================

    [Fact]
    public async Task Novo_DeveCadastrarEstoqueMinimo()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        // O form manda o estoque minimo no mesmo formato da quantidade.
        controller.Novo("LEITE", "Prateleira", "40", "599", null, "10");

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(10m, salvo.EstoqueMinimo);
    }

    // Sem estoque minimo enviado, o produto nasce NULL (= nunca avisa).
    [Fact]
    public async Task Novo_SemEstoqueMinimo_DeveGravarNulo()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("LEITE", "Prateleira", "40", "599", null, null);

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Null(salvo.EstoqueMinimo);
    }

    // Estoque minimo negativo e recusado (mesma politica do preco/quantidade).
    [Fact]
    public async Task Novo_DeveRejeitarEstoqueMinimoNegativo()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("LEITE", "Prateleira", "40", "599", null, "-5");

        Assert.Empty(qa.Context.Produtos.AsNoTracking().ToList());
    }

    [Theory]
    [InlineData(4, 10, true)]      // 4 < 10   -> alerta
    [InlineData(10, 10, false)]    // 10 == 10 -> igual nao e "abaixo"
    [InlineData(50, 10, false)]    // 50 > 10  -> folgado
    [InlineData(0.5, 1, true)]     // fracionado: 0,5 < 1 -> alerta
    [InlineData(9, 10, true)]      // o caso que a collation BINARY erraria
    public void EstoqueAbaixoDoMinimo_DeveUsarComparacaoNumerica(decimal quantidade, decimal minimo, bool esperado)
    {
        var produto = new Produto { Quantidade = quantidade, EstoqueMinimo = minimo };

        Assert.Equal(esperado, produto.EstoqueAbaixoDoMinimo);
    }

    // Sem minimo definido (null ou 0) NUNCA gera alerta - mesmo com estoque 0.
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void EstoqueMinimoNaoDefinido_NuncaGeraAlerta(double? minimo)
    {
        var produto = new Produto { Quantidade = 0m, EstoqueMinimo = (decimal?)minimo };

        Assert.False(produto.EstoqueAbaixoDoMinimo);
    }

    // Estoque zerado com minimo > 0: e o alerta mais importante (produto acabou).
    [Fact]
    public void EstoqueZerado_ComMinimo_DeveAlertar()
    {
        var produto = new Produto { Quantidade = 0m, EstoqueMinimo = 3m };

        Assert.True(produto.EstoqueAbaixoDoMinimo);
    }

    // Editar nao pode ZERAR o minimo configurado quando o campo nao vem no POST
    // (mesma politica do BUG-07, que zerava o estoque a cada edicao).
    [Fact]
    public async Task Editar_SemEstoqueMinimoEnviado_DevePreservarOConfigurado()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        produto.EstoqueMinimo = 10m;
        qa.Context.SaveChanges();

        var controller = new HomeController(qa.Context).ComTempData();
        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "599", null);

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(10m, salvo.EstoqueMinimo);
    }

    [Fact]
    public async Task Editar_ComEstoqueMinimoValido_DeveGravar()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "599", "25");

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(25m, salvo.EstoqueMinimo);
    }
}
