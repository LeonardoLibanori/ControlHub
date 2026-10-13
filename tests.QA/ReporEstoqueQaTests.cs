using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using static tests.QA.JsonAssertExtensions;

namespace tests.QA;

/// <summary>
/// CARACTERIZAÇÃO - Botão Repor Estoque (HomeController.Repor).
/// </summary>
public sealed class ReporEstoqueQaTests
{
    // ---------- 1. SOMA / PESO / ESPAÇOS ----------
    [Fact]
    public async Task Repor_SomaNumerica_Prateleira()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira, estoqueMinimo: 5);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "5") as JsonResult;

        Assert.True(result.IsSuccess());
        Assert.Equal(15d, result.GetNum("novoEstoque"));
        Assert.Equal("un", result.GetStr("unidade"));
    }

    [Fact]
    public async Task Repor_SomaNumerica_Peso()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "QUEIJO", preco: 100m, quantidade: 2.5m, tipo: TipoProduto.PorPeso, estoqueMinimo: 1);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "0,125") as JsonResult;

        Assert.True(result.IsSuccess());
        Assert.Equal(2.625d, result.GetNum("novoEstoque"));
        Assert.Equal("kg", result.GetStr("unidade"));
    }

    [Fact]
    public async Task Repor_PontoOuVirgula_MesmoResultado()
    {
        using var qa = new QaContext();
        var p1 = qa.SeedProduto(nome: "P1", preco: 100m, quantidade: 2.5m, tipo: TipoProduto.PorPeso);
        var p2 = qa.SeedProduto(nome: "P2", preco: 100m, quantidade: 2.5m, tipo: TipoProduto.PorPeso);
        var controller = new HomeController(qa.Context).ComTempData();

        var r1 = await controller.Repor(p1.Id, "0.125") as JsonResult;
        var r2 = await controller.Repor(p2.Id, "0,125") as JsonResult;

        Assert.Equal(r1.GetNum("novoEstoque"), r2.GetNum("novoEstoque"));
    }

    [Fact]
    public async Task Repor_EspacosNoInput_SaoIgnorados()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, " 3 ") as JsonResult;

        Assert.True(result.IsSuccess());
        Assert.Equal(13d, result.GetNum("novoEstoque"));
    }

    [Fact]
    public async Task Repor_ValorRelido_EExatoDecimalGravado()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "7") as JsonResult;
        var novoEstoque = result.GetNum("novoEstoque");

        // releitura via EF - busca o produto no mesmo contexto
        var produtoDb = await qa.Context.Produtos.FirstAsync(x => x.Id == p.Id);
        Assert.Equal((decimal)novoEstoque, produtoDb.Quantidade);
    }

    // ---------- 2. RECUSAS SEM ALTERAR NADA ----------
    [Theory]
    [InlineData("", "Informe uma quantidade maior que zero.")]
    [InlineData("abc", "Quantidade inválida. Use apenas números.")]
    [InlineData("0", "Informe uma quantidade maior que zero.")]
    [InlineData("-1", "Informe uma quantidade maior que zero.")]
    [InlineData("1000001", "A quantidade máxima por reposição é 1.000.000.")]
    [InlineData("1,2345", "Use no máximo 3 casas decimais.")]
    [InlineData("1e3", "Quantidade inválida. Use apenas números.")]
    [InlineData("1.000,5", "Quantidade inválida. Use apenas números.")]
    public async Task Repor_EntradaInvalida_RecusaSemAlterar(string input, string mensagemEsperada)
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira, estoqueMinimo: 5);
        var controller = new HomeController(qa.Context).ComTempData();

        var antesEstoque = p.Quantidade;
        var antesPreco = p.Preco;
        var antesMinimo = p.EstoqueMinimo;
        var antesNome = p.Nome;
        var antesTipo = p.Tipo;
        var antesAtivo = p.Ativo;
        var countHistoricoAntes = await qa.Context.HistoricoPrecos.CountAsync();
        var countVendasAntes = await qa.Context.Vendas.CountAsync();
        var countCaixaAntes = await qa.Context.CaixaMovimentacoes.CountAsync();

        var result = await controller.Repor(p.Id, input) as JsonResult;

        Assert.False(result.IsSuccess());
        Assert.Equal(mensagemEsperada, result.GetStr("mensagem"));

        // nada alterado
        var produtoDb = await qa.Context.Produtos.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(antesEstoque, produtoDb.Quantidade);
        Assert.Equal(antesPreco, produtoDb.Preco);
        Assert.Equal(antesMinimo, produtoDb.EstoqueMinimo);
        Assert.Equal(antesNome, produtoDb.Nome);
        Assert.Equal(antesTipo, produtoDb.Tipo);
        Assert.Equal(antesAtivo, produtoDb.Ativo);
        Assert.Equal(countHistoricoAntes, await qa.Context.HistoricoPrecos.CountAsync());
        Assert.Equal(countVendasAntes, await qa.Context.Vendas.CountAsync());
        Assert.Equal(countCaixaAntes, await qa.Context.CaixaMovimentacoes.CountAsync());
    }

    [Fact]
    public async Task Repor_FracionadoEmPrateleira_Recusa()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "1,5") as JsonResult;

        Assert.False(result.IsSuccess());
        Assert.Equal("Este produto é vendido por unidade: informe um número inteiro.", result.GetStr("mensagem"));
        var produtoDb = await qa.Context.Produtos.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(10m, produtoDb.Quantidade);
    }

    [Fact]
    public async Task Repor_IdInexistente_Recusa()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(999999, "5") as JsonResult;

        Assert.False(result.IsSuccess());
        Assert.Equal("Produto não encontrado.", result.GetStr("mensagem"));
    }

    [Fact]
    public async Task Repor_ProdutoInativo_Recusa()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira);
        p.Ativo = false;
        qa.Context.SaveChanges();
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "5") as JsonResult;

        Assert.False(result.IsSuccess());
        Assert.Equal("Produto não encontrado.", result.GetStr("mensagem"));
    }

    // ---------- 3. ESTOQUE ABAIXO DO MINIMO REAVALIADO ----------
    [Fact]
    public async Task Repor_EstoqueAbaixoMinimo_DeixaDeSerBaixo()
    {
        using var qa = new QaContext();
        // estoque 2, minimo 5 -> abaixo do minimo
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 2m, tipo: TipoProduto.Prateleira, estoqueMinimo: 5);
        Assert.True(p.EstoqueAbaixoDoMinimo);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "10") as JsonResult;

        Assert.True(result.IsSuccess());
        var produtoDb = await qa.Context.Produtos.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(12m, produtoDb.Quantidade);
        Assert.False(produtoDb.EstoqueAbaixoDoMinimo);
    }

    [Fact]
    public async Task Repor_ChegaExatoNoMinimo_DeixaDeSerBaixo()
    {
        using var qa = new QaContext();
        // estoque 9, minimo 10 -> abaixo do minimo
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 9m, tipo: TipoProduto.Prateleira, estoqueMinimo: 10);
        Assert.True(p.EstoqueAbaixoDoMinimo);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "1") as JsonResult;

        Assert.True(result.IsSuccess());
        var produtoDb = await qa.Context.Produtos.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(10m, produtoDb.Quantidade);
        Assert.False(produtoDb.EstoqueAbaixoDoMinimo);
    }

    // ---------- 4. JSON RESPOSTA ----------
    [Fact]
    public async Task Repor_Sucesso_JsonContemCamposEsperados()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.Prateleira);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "5") as JsonResult;

        Assert.True(result.IsSuccess());
        Assert.NotNull(result.GetProp("novoEstoque"));
        Assert.NotNull(result.GetProp("unidade"));
        Assert.Equal("un", result.GetStr("unidade"));
        Assert.True(result.GetNum("novoEstoque") > 10);
    }

    [Fact]
    public async Task Repor_Peso_UnidadeKg()
    {
        using var qa = new QaContext();
        var p = qa.SeedProduto(nome: "TESTE", preco: 10m, quantidade: 10m, tipo: TipoProduto.PorPeso);
        var controller = new HomeController(qa.Context).ComTempData();

        var result = await controller.Repor(p.Id, "5") as JsonResult;

        Assert.Equal("kg", result.GetStr("unidade"));
    }

    // ---------- 5. AUTORIZAÇÃO POR REFLEXÃO ----------
    [Fact]
    public void ReporAction_TemAtributosCorretos()
    {
        var metodo = typeof(HomeController).GetMethod("Repor");
        Assert.NotNull(metodo);

        var httpPost = metodo.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);

        var validateAntiForgery = metodo.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();
        Assert.NotNull(validateAntiForgery);

        var authorize = metodo.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("PodeGerenciarEstoque", authorize.Policy);
    }

    // ---------- 6. VARREDURA DE CONTROLLERS SEM [AUTHORIZE] CONTINUA VERDE ----------
    [Fact]
    public void Controllers_TodosTemAuthorize()
    {
        var assembly = typeof(HomeController).Assembly;
        var controllers = assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var semAuthorize = controllers
            .Where(t => t.GetCustomAttribute<AuthorizeAttribute>() is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(semAuthorize.Count == 0,
            "Controllers sem [Authorize]: " + string.Join(", ", semAuthorize));
    }

    [Fact]
    public void PostActions_TodasTemValidateAntiForgeryToken()
    {
        var controllers = new[] { typeof(CaixaController), typeof(HomeController) };

        var semToken = new List<string>();

        foreach (var ctrl in controllers)
        {
            var actions = ctrl.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null)
                .Where(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is null);

            foreach (var a in actions)
                semToken.Add($"{ctrl.Name}.{a.Name}");
        }

        Assert.True(semToken.Count == 0,
            "POSTs sem [ValidateAntiForgeryToken]: " + string.Join(", ", semToken));
    }
}