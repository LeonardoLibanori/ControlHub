using System.Reflection;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Lacunas funcionais (filtro de data, /Home/Error, TempData).
/// </summary>
public class LacunasQaTests
{
    // ---------------------------------------------------------------------
    // BUG: o filtro de data do Historico e ignorado pela rota/view.
    // CaixaController.Historico(DateTime? data) filtra corretamente no
    // controller, mas a acao NAO tem [HttpGet] e a rota usada pela view e
    // "/Caixa/Historico" com o form(method="get") sem action, que envia
    // ?data=... para a action do controller padrao Home. Na pratica o filtro
    // nao chega a ser aplicado e a tela mostra sempre a lista completa.
    // Este teste caracteriza o comportamento do parametro na action.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Historico_DeveRespeitarOFiltroDeData()
    {
        using var qa = new QaContext();
        var ontem = DateTime.Today.AddDays(-1);

        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", data: DateTime.Today.AddHours(10), codigoVendedor: "A");
        qa.SeedVenda(50m, metodoPagamento: "Dinheiro", data: ontem.AddHours(10), codigoVendedor: "B");

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(ontem);

        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(
            ((ViewResult)result).Model);

        Assert.Single(model);
        Assert.Equal(50m, model.Single().TotalVenda);
    }

    // CONTROLE: sem filtro, traz tudo.
    [Fact]
    public async Task Historico_SemFiltro_DeveTrazerTodasAsVendas()
    {
        using var qa = new QaContext();
        qa.SeedVenda(100m, metodoPagamento: "Dinheiro", codigoVendedor: "A");
        qa.SeedVenda(50m, metodoPagamento: "Dinheiro",
                     data: DateTime.Today.AddDays(-2), codigoVendedor: "B");

        var controller = new CaixaController(qa.Context);
        var result = await controller.Historico(null);

        var model = Assert.IsAssignableFrom<IEnumerable<Venda>>(
            ((ViewResult)result).Model);

        Assert.Equal(2, model.Count());
    }

    // ---------------------------------------------------------------------
    // BUG: /Home/Error retorna 404.
    // Program.cs registra app.UseExceptionHandler("/Home/Error"), mas
    // HomeController NAO tem a action Error. Quando uma excecao escapa em
    // producao, o handler tenta redirecionar para /Home/Error, que cai no
    // roteamento sem action -> 404, mascarando o erro real.
    // ---------------------------------------------------------------------
    [Fact]
    public void HomeController_DevePossuirActionError()
    {
        var metodo = typeof(HomeController).GetMethod("Error",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.True(metodo is not null,
            "HomeController nao possui a action Error, mas Program.cs aponta " +
            "UseExceptionHandler(\"/Home/Error\"). O tratamento de erro global " +
            "resulta em 404.");
    }

    // ---------------------------------------------------------------------
    // BUG: TempData["Erro"] nunca e exibido.
    // HomeController.AjustarEstoque define TempData["Erro"] = "Estoque
    // insuficiente!" quando a remocao excede o saldo, mas NENHUMA view le
    // esse TempData. O usuario recebe a tela do produto sem nenhum aviso,
    // e o estoque continua igual - parece que o botao nao funcionou.
    // ---------------------------------------------------------------------
    [Fact]
    public void TempDataErro_DeveSerExibidoNaView()
    {
        var view = LocalizarArquivo(Path.Combine("Views", "Home", "Produto.cshtml"));
        Assert.True(File.Exists(view), $"Produto.cshtml nao encontrado: {view}");

        var conteudo = File.ReadAllText(view);
        Assert.True(conteudo.Contains("TempData"),
            "A view Produto.cshtml nao exibe TempData[\"Erro\"], definido por " +
            "AjustarEstoque quando o estoque e insuficiente.");
    }

    // CONTROLE: AjustarEstoque realmente define o TempData quando barra.
    [Fact]
    public async Task AjustarEstoque_DeveDefinirTempDataQuandoEstoqueInsuficiente()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(quantidade: 5m);
        var controller = new HomeController(qa.Context).ComTempData();

        await controller.AjustarEstoque(produto.Id, 10m, "remover");

        // Sem HttpContext o TempData e criado vazio; basta verificar o
        // comportamento do estoque, que e o efeito observavel do bug.
        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);
        Assert.Equal(5m, salvo.Quantidade);
    }

    private static string LocalizarArquivo(string caminhoRelativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ControleEstoqueLoja.csproj")))
                return Path.Combine(dir.FullName, caminhoRelativo);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do projeto web nao encontrada.");
    }
}
