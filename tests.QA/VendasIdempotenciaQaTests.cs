using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja.Data;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ControleEstoqueLoja;

namespace tests.QA;

public class VendasIdempotenciaQaTests
{
    private static List<CaixaController.ItemVendaSimples> Item(int id, string quantidade = "1") => new()
    {
        new CaixaController.ItemVendaSimples { ProdutoId = id.ToString(), Quantidade = quantidade, Preco = "9999" }
    };

    [Fact]
    public async Task MesmaChaveEMesmoPedido_DevolveVendaOriginalSemBaixarEstoqueDuasVezes()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(preco: 5m, quantidade: 3m);
        const string chave = "d4f651b0-9713-44a7-bd89-700118133bbc";
        var controller = new CaixaController(qa.Context);

        var primeira = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, Item(produto.Id), chave);
        var originalId = primeira.GetNum("vendaId");
        var repetida = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, Item(produto.Id), chave);

        Assert.True(primeira.IsSuccess());
        Assert.True(repetida.IsSuccess());
        Assert.Equal(originalId, repetida.GetNum("vendaId"));
        Assert.True((bool)repetida.GetProp("repetida")!);
        Assert.Equal(2m, await qa.Context.Produtos.Where(p => p.Id == produto.Id).Select(p => p.Quantidade).SingleAsync());
        Assert.Single(await qa.Context.Vendas.ToListAsync());
    }

    [Fact]
    public async Task MesmaChaveComPedidoDiferente_Retorna409ESemMutacao()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(preco: 5m, quantidade: 5m);
        const string chave = "7a3810d1-4789-4a57-a25a-1a0496ff64dd";
        var controller = new CaixaController(qa.Context);
        Assert.True((await controller.FinalizarVenda("Pix", null, "", 0m, 0m, Item(produto.Id), chave)).IsSuccess());

        var conflito = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, Item(produto.Id, "2"), chave);

        Assert.IsType<ConflictObjectResult>(conflito);
        Assert.Equal(4m, await qa.Context.Produtos.Where(p => p.Id == produto.Id).Select(p => p.Quantidade).SingleAsync());
        Assert.Single(await qa.Context.Vendas.ToListAsync());
    }

    [Fact]
    public async Task FalhaAntesDaGravacao_PermiteRepetirMesmaChaveAposCorrigirEstoque()
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(preco: 5m, quantidade: 0m);
        const string chave = "f05432ae-3671-4dcf-a654-5c7ca475cd5f";
        var controller = new CaixaController(qa.Context);
        var pedido = Item(produto.Id);

        var recusada = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, pedido, chave);
        Assert.False(recusada.IsSuccess());
        Assert.Empty(await qa.Context.Vendas.ToListAsync());
        Assert.Equal(0m, await qa.Context.Produtos.Where(p => p.Id == produto.Id).Select(p => p.Quantidade).SingleAsync());

        await qa.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Produtos SET Quantidade = '1' WHERE Id = {produto.Id}");
        qa.Context.ChangeTracker.Clear();
        var aceita = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, pedido, chave);

        Assert.True(aceita.IsSuccess());
        Assert.Equal(0m, await qa.Context.Produtos.Where(p => p.Id == produto.Id).Select(p => p.Quantidade).SingleAsync());
        Assert.Single(await qa.Context.Vendas.ToListAsync());
    }

    [Fact]
    public async Task DuasRequisicoesSimultaneasComMesmaChave_GravamUmaVendaEReplayRecuperaOriginal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qa-idem-race-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            int produtoId;
            using (var setup = new ApplicationDbContext(options))
            {
                setup.Database.EnsureCreated();
                var produto = new Produto { Nome = "Produto corrida idem", Preco = 5m, Quantidade = 2m, Tipo = TipoProduto.Prateleira, Ativo = true };
                setup.Produtos.Add(produto);
                setup.SaveChanges();
                produtoId = produto.Id;
            }

            using var ctx1 = new ApplicationDbContext(options);
            using var ctx2 = new ApplicationDbContext(options);
            const string chave = "ab90016a-9255-4ee8-86fd-cb0af150fda8";
            var primeiro = new CaixaController(ctx1).FinalizarVenda("Pix", null, "", 0m, 0m, Item(produtoId), chave);
            var segundo = new CaixaController(ctx2).FinalizarVenda("Pix", null, "", 0m, 0m, Item(produtoId), chave);
            var resultados = await Task.WhenAll(primeiro, segundo);

            Assert.All(resultados, resultado => Assert.True(resultado.IsSuccess()
                || resultado is ObjectResult { StatusCode: 503 }));
            var replay = await new CaixaController(ctx2).FinalizarVenda("Pix", null, "", 0m, 0m, Item(produtoId), chave);
            Assert.True(replay.IsSuccess());
            Assert.Equal(1m, await ctx2.Produtos.AsNoTracking().Where(p => p.Id == produtoId).Select(p => p.Quantidade).SingleAsync());
            Assert.Single(await ctx2.Vendas.AsNoTracking().ToListAsync());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task RespostaPerdidaDepoisDoCommit_RepeticaoRecuperaVendaSemBaixaDuplicada()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qa-idem-resposta-perdida-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        try
        {
            int produtoId;
            using (var setup = new ApplicationDbContext(options))
            {
                setup.Database.EnsureCreated();
                var produto = new Produto { Nome = "Produto resposta perdida", Preco = 7m, Quantidade = 4m, Tipo = TipoProduto.Prateleira, Ativo = true };
                setup.Produtos.Add(produto);
                setup.SaveChanges();
                produtoId = produto.Id;
            }

            using var context = new ApplicationDbContext(options);
            var controller = new CaixaController(context);
            const string chave = "ac084df7-6e70-47e3-a343-d886bb235b52";
            var pedido = Item(produtoId, "2");

            // Shim de transporte: deixa a action concluir e o commit terminar,
            // verifica o arquivo por outra conexão e então descarta a resposta.
            var perdida = await Assert.ThrowsAsync<HttpRequestException>(() =>
                SimularPerdaDeRespostaAsync(
                    () => controller.FinalizarVenda("Pix", null, "", 0m, 0m, pedido, chave),
                    async respostaServidor =>
                    {
                        Assert.True(respostaServidor.IsSuccess());
                        using var observador = new ApplicationDbContext(options);
                        Assert.Equal(2m, await observador.Produtos.AsNoTracking().Where(p => p.Id == produtoId).Select(p => p.Quantidade).SingleAsync());
                        Assert.Single(await observador.Vendas.AsNoTracking().ToListAsync());
                        Assert.Single(await observador.VendaItens.AsNoTracking().ToListAsync());
                    }));
            Assert.Contains("resposta", perdida.Message, StringComparison.OrdinalIgnoreCase);

            var recuperada = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, pedido, chave);
            Assert.True(recuperada.IsSuccess());
            Assert.True((bool)recuperada.GetProp("repetida")!);
            using var final = new ApplicationDbContext(options);
            Assert.Equal(2m, await final.Produtos.AsNoTracking().Where(p => p.Id == produtoId).Select(p => p.Quantidade).SingleAsync());
            Assert.Single(await final.Vendas.AsNoTracking().ToListAsync());
            Assert.Single(await final.VendaItens.AsNoTracking().ToListAsync());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task SqliteBusy_Retorna503EPermiteRetryComMesmaChaveDepoisDoDesbloqueio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qa-idem-busy-{Guid.NewGuid():N}.db");
        // Timeout SQLite explícito e curto: 0 significa espera ilimitada no provider.
        var connectionString = $"Data Source={path};Pooling=False;Default Timeout=1";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
        try
        {
            int produtoId;
            using (var setup = new ApplicationDbContext(options))
            {
                setup.Database.EnsureCreated();
                var produto = new Produto { Nome = "Produto busy", Preco = 3m, Quantidade = 2m, Tipo = TipoProduto.Prateleira, Ativo = true };
                setup.Produtos.Add(produto);
                setup.SaveChanges();
                produtoId = produto.Id;
            }

            using var context = new ApplicationDbContext(options);
            var controller = new CaixaController(context);
            const string chave = "25a223d5-d8bb-41d8-98ed-b0b40524f495";
            using (var lockConnection = new SqliteConnection(connectionString))
            {
                await lockConnection.OpenAsync();
                using var lockTransaction = lockConnection.BeginTransaction(deferred: false);
                var busy = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, Item(produtoId), chave);
                Assert.IsType<ObjectResult>(busy);
                Assert.Equal(503, ((ObjectResult)busy).StatusCode);
            }

            var retry = await controller.FinalizarVenda("Pix", null, "", 0m, 0m, Item(produtoId), chave);
            Assert.True(retry.IsSuccess());
            using var final = new ApplicationDbContext(options);
            Assert.Equal(1m, await final.Produtos.AsNoTracking().Where(p => p.Id == produtoId).Select(p => p.Quantidade).SingleAsync());
            Assert.Single(await final.Vendas.AsNoTracking().ToListAsync());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static async Task<T> SimularPerdaDeRespostaAsync<T>(Func<Task<T>> actionNoServidor, Func<T, Task> observarCommit)
    {
        var resposta = await actionNoServidor();
        await observarCommit(resposta);
        throw new HttpRequestException("Resposta HTTP perdida depois do commit.", null, HttpStatusCode.RequestTimeout);
    }

    [Fact]
    public async Task MigrationDeIdempotencia_EAdicionalEPreservaVendaLegada()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qa-idempotencia-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ControleEstoqueLoja.Data.ApplicationDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        try
        {
            using (var db = new ControleEstoqueLoja.Data.ApplicationDbContext(options))
                db.Database.Migrate("20261001204929_HistoricoPrecoProduto");
            using (var db = new ControleEstoqueLoja.Data.ApplicationDbContext(options))
            {
                await db.Database.ExecuteSqlRawAsync("INSERT INTO Vendas (CPFCliente, CodigoVendedor, DataVenda, IsFechado, MetodoPagamento, TotalVenda, ValorRecebido, ValorTroco) VALUES (NULL, NULL, '2026-10-01 10:00:00', 0, 'Pix', 500, 500, 0)");
                db.Database.Migrate();
                Assert.Equal(1, await db.Vendas.CountAsync());
                var legada = await db.Vendas.SingleAsync();
                Assert.Null(legada.ChaveIdempotencia);
                Assert.Null(legada.HashConteudo);
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_index_list('Vendas') WHERE name = 'IX_Vendas_ChaveIdempotencia' AND \"unique\" = 1").SingleAsync());
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task ConsultaDeStatus_ExigePoliticaDoPdvEExpõeSomenteEstadoMinimo()
    {
        var politicaController = typeof(CaixaController).GetCustomAttribute<AuthorizeAttribute>();
        var acao = typeof(CaixaController).GetMethod(nameof(CaixaController.ConsultarStatusVenda))!;
        var politicaAcao = acao.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(politicaController);
        Assert.NotNull(politicaAcao);
        Assert.Equal(Policies.PodeOperarPDV, politicaAcao.Policy);

        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);
        var chaveInvalida = await controller.ConsultarStatusVenda("invalida", CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(chaveInvalida);

        var inexistente = await controller.ConsultarStatusVenda("399cbff5-e5a9-44eb-b204-dac50731dd98", CancellationToken.None);
        Assert.True(inexistente.IsSuccess());
        Assert.False((bool)inexistente.GetProp("encontrada")!);
        Assert.Null(inexistente.GetProp("vendaId"));
        Assert.IsType<JsonResult>(inexistente);
        var payload = ((JsonResult)inexistente).Value!;
        Assert.Equal(new[] { "encontrada", "success", "vendaId" }, payload.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }
}
