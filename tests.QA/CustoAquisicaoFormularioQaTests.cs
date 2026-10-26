using ControleEstoqueLoja.Controllers;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

public class CustoAquisicaoFormularioQaTests
{
    [Fact]
    public void Novo_ConverteCentavosEPersisteCustoDeAquisicao()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo(
            "CAFÉ",
            "Prateleira",
            "2",
            "1250",
            codigoBarras: null,
            unidadeMedida: "0",
            custoAquisicao: "500");

        var produto = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(5m, produto.CustoAquisicao);
    }

    [Fact]
    public void Novo_SemCustoInformado_PersisteZeroDoContratoAtual()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("CAFÉ", "Prateleira", "2", "1250", codigoBarras: null);

        var produto = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(0m, produto.CustoAquisicao);
    }

    [Fact]
    public void Editar_CustoVazioPreservaValorAtualEValorInformadoAtualiza()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "CAFÉ");
        produto.CustoAquisicao = 8.25m;
        qa.Context.SaveChanges();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(produto.Id, "CAFÉ", "Prateleira", quantidade: null, preco: "1250", custoAquisicao: "");
        Assert.Equal(8.25m, qa.Context.Produtos.AsNoTracking().Single().CustoAquisicao);

        controller.Editar(produto.Id, "CAFÉ", "Prateleira", quantidade: null, preco: "1250", custoAquisicao: "725");
        Assert.Equal(7.25m, qa.Context.Produtos.AsNoTracking().Single().CustoAquisicao);
    }
}
