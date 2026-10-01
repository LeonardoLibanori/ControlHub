using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// FASE A - ITEM 2: Historico de preco (HomeController.Editar).
/// Log informativo para o dono responder "isso antes custava quanto?".
/// Nao participa de nenhum calculo de venda, saldo ou fechamento.
/// </summary>
public class HistoricoPrecoQaTests
{
    // Mudou o preco -> 1 linha, com anterior e novo corretos.
    [Fact]
    public async Task Editar_ComPrecoDiferente_DeveGravarHistorico()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        // O form manda centavos em string: "749" = R$ 7,49.
        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "749", null);

        var registro = qa.Context.HistoricoPrecos.AsNoTracking().Single();
        Assert.Equal(produto.Id, registro.ProdutoId);
        Assert.Equal(5.99m, registro.PrecoAnterior);
        Assert.Equal(7.49m, registro.PrecoNovo);
    }

    // Salvou SEM mexer no preco -> NAO grava linha (nao suja o historico).
    [Fact]
    public async Task Editar_SemMudarPreco_NaoDeveGravarHistorico()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        // Mesmo preco (599 centavos), so mudando o nome.
        controller.Editar(produto.Id, "LEITE 1L", "Prateleira", null, "599", null);

        Assert.Empty(qa.Context.HistoricoPrecos.AsNoTracking().ToList());
    }

    // =====================================================================
    // HISTORICO FANTASMA: um POST REJEITADO por qualquer validacao nao pode
    // deixar linha de historico nem alterar o preco. A gravacao fica DEPOIS
    // de todas as validacoes e imediatamente antes do SaveChanges.
    // =====================================================================

    // Recusa por estoque minimo NEGATIVO: validacao que acontece DEPOIS da
    // atribuicao do preco. Sem o cuidado, o preco ja estaria alterado em
    // memoria e o historico poderia ser gravado.
    [Fact]
    public async Task Editar_RejeitadoPorEstoqueMinimoNegativo_NaoDeveGravarHistorico()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "749", "-5");

        Assert.Empty(qa.Context.HistoricoPrecos.AsNoTracking().ToList());

        // Nem o preco pode ter mudado: o cadastro foi recusado por inteiro.
        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(5.99m, salvo.Preco);
    }

    // Mesma garantia quando a recusa vem da quantidade negativa.
    [Fact]
    public async Task Editar_RejeitadoPorQuantidadeNegativa_NaoDeveGravarHistorico()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(produto.Id, "LEITE", "Prateleira", "-3", "749", null);

        Assert.Empty(qa.Context.HistoricoPrecos.AsNoTracking().ToList());

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(5.99m, salvo.Preco);
        Assert.Equal(40m, salvo.Quantidade);
    }

    // Preco negativo no cadastro novo: recusado, e nada de historico.
    [Fact]
    public async Task Novo_RejeitadoPorPrecoNegativo_DeveDeixarHistoricoVazio()
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Novo("LEITE", "Prateleira", "40", "-500", null, "10");

        Assert.Empty(qa.Context.HistoricoPrecos.AsNoTracking().ToList());
        Assert.Empty(qa.Context.Produtos.AsNoTracking().ToList());
    }

    // Sequencia real: varias trocas ficam todas registradas e ENCADEADAS
    // (o "novo" de uma linha e o "anterior" da seguinte).
    [Fact]
    public async Task Editar_SequenciaDeAltancas_DeveRegistrarTodasEmCadeia()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "749", null);   // 7,49
        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "699", null);   // 6,99
        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "899", null);   // 8,99

        var registros = qa.Context.HistoricoPrecos.AsNoTracking().OrderBy(h => h.Id).ToList();
        Assert.Equal(3, registros.Count);

        Assert.Equal(5.99m, registros[0].PrecoAnterior);
        Assert.Equal(7.49m, registros[0].PrecoNovo);
        Assert.Equal(7.49m, registros[1].PrecoAnterior);
        Assert.Equal(6.99m, registros[1].PrecoNovo);
        Assert.Equal(6.99m, registros[2].PrecoAnterior);
        Assert.Equal(8.99m, registros[2].PrecoNovo);
    }

    // Preco gravado em CENTAVOS no banco (INTEGER), nunca em REAL/TEXT:
    // R$ 5,99 -> 599. Mesmo contrato do BUG-20 para dinheiro. Usa DbConnection
    // porque SqlQueryRaw embrulha o SQL em {s.Value} e o SQLite rejeita.
    [Fact]
    public async Task Editar_DeveGravarPrecoEmCentavosNoBanco()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(produto.Id, "LEITE", "Prateleira", null, "749", null);

        Assert.Equal(5.99m, qa.Context.HistoricoPrecos.AsNoTracking().Single().PrecoAnterior);

        var conexao = qa.Context.Database.GetDbConnection();
        conexao.Open();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.CommandText = "SELECT typeof(PrecoAnterior) FROM HistoricoPrecos LIMIT 1";
            Assert.Equal("integer", cmd.ExecuteScalar());
        }
    }

    // Historico e por produto, mais recente primeiro: o de outro nao aparece.
    [Fact]
    public async Task Historico_DeveListarMaisRecentePrimeiroDoProdutoCerto()
    {
        using var qa = new QaContext();
        var leite = qa.SeedProduto(nome: "LEITE", preco: 5.99m, quantidade: 40m);
        var acucar = qa.SeedProduto(nome: "ACUCAR", preco: 4.89m, quantidade: 90m);
        var controller = new HomeController(qa.Context).ComTempData();

        controller.Editar(leite.Id, "LEITE", "Prateleira", null, "749", null);
        controller.Editar(acucar.Id, "ACUCAR", "Prateleira", null, "699", null);
        controller.Editar(leite.Id, "LEITE", "Prateleira", null, "899", null);

        var view = (ViewResult)controller.Produto(leite.Id);

        Assert.NotNull(view.ViewData);
        var historico = (List<HistoricoPreco>?)view.ViewData["HistoricoPrecos"];
        Assert.NotNull(historico);
        Assert.Equal(2, historico.Count);
        Assert.All(historico, h => Assert.Equal(leite.Id, h.ProdutoId));
        Assert.Equal(8.99m, historico[0].PrecoNovo);
        Assert.Equal(7.49m, historico[1].PrecoNovo);
    }
}