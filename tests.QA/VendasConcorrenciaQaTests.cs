using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - CONCORRENCIA (duas vendas simultaneas do mesmo produto).
/// Recuperado do notebook antigo (2026-09-29) e adaptado ao QaContext atual.
///
/// Os dois testes apontam para a MESMA causa raiz: FinalizarVenda nao roda em
/// transacao, nao usa lock e nao rechecka o estoque antes de gravar. Duas
/// requisicoes simultaneas leem o mesmo saldo e o ultimo SaveChanges sobrescreve
/// o do outro (venda fantasma / perda de estoque). A correcao pertence a Fase 2
/// (BUG-05: transacao + controle de concorrencia na baixa de estoque).
/// </summary>
public class VendasConcorrenciaQaTests
{
    /// <summary>
    /// Cria um ApplicationDbContext sobre o mesmo arquivo .db dos demais.
    /// O QaContext NAO expoe o caminho do banco (e nao precisa expor: aqui cada
    /// controller precisa do SEU proprio contexto, apontando para o MESMO banco).
    /// </summary>
    private static ApplicationDbContext NovoContexto(string dbPath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static List<CaixaController.ItemVendaSimples> Itens(int produtoId, string qtd, string preco)
        => new()
        {
            new CaixaController.ItemVendaSimples
            {
                ProdutoId = produtoId.ToString(),
                Quantidade = qtd,
                Preco = preco
            }
        };

    /// <summary>Semeia o cenario base: 1 vendedor ativo + 1 produto ativo.</summary>
    private static int Semear(string dbPath, string nomeProduto, string codigoBarras,
                              decimal quantidade, string codigoVendedor)
    {
        using var setup = NovoContexto(dbPath);
        setup.Database.EnsureCreated();

        // BUG-08: FinalizarVenda so aceita produto ATIVO (soft delete).
        var produto = new Produto
        {
            Nome = nomeProduto,
            Preco = 5m,
            Quantidade = quantidade,
            Tipo = TipoProduto.Prateleira,
            Ativo = true,
            CodigoBarras = codigoBarras
        };
        setup.Produtos.Add(produto);
        setup.SaveChanges();          // o Id so existe DEPOIS do SaveChanges
        int produtoId = produto.Id;

        setup.Vendedores.Add(new Vendedor
        {
            Nome = "VENDEDOR CONC",
            Codigo = codigoVendedor,
            Ativo = true
        });
        setup.SaveChanges();

        return produtoId;
    }

    /// <summary>Le estoque e quantidade de vendas direto do arquivo .db.</summary>
    private static (decimal estoque, int vendas, List<decimal> totais) Conferir(string dbPath, int produtoId)
    {
        using var check = NovoContexto(dbPath);
        return (
            check.Produtos.AsNoTracking().First(p => p.Id == produtoId).Quantidade,
            check.Vendas.AsNoTracking().Count(),
            check.Vendas.AsNoTracking().Select(v => v.TotalVenda).ToList());
    }

    private static void ApagarBanco(string dbPath)
    {
        try
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var f = dbPath + suffix;
                if (File.Exists(f)) File.Delete(f);
            }
        }
        catch (IOException)
        {
            // Arquivo temporario bloqueado pelo SQLite; irrelevante para o teste.
        }
    }
    // ---------------------------------------------------------------------
    // BUG-05: estoque 1 + duas vendas simultaneas de 1 un nao podem
    // gerar 2 vendas (nem zerar o estoque duas vezes).
    // DEVE FALHAR enquanto nao houver transacao/lock/rechecagem.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task VendasSimultaneas_NaoPodemVenderMaisQueOEstoque()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"qa-conc-{Guid.NewGuid():N}.db");
        int produtoId = Semear(dbPath, "PRODUTO CONCORRENTE", "7891000000012", 1m, "CONC1");

        // Dois contextos INDEPENDENTES sobre o MESMO banco = duas "requisicoes" simultaneas.
        var ctx1 = NovoContexto(dbPath);
        var ctx2 = NovoContexto(dbPath);

        var responses = new List<string>();
        try
        {
            var res = await Task.WhenAll(
                new CaixaController(ctx1).FinalizarVenda("Dinheiro", "", "CONC1", 5m, 0m, Itens(produtoId, "1", "5")),
                new CaixaController(ctx2).FinalizarVenda("Dinheiro", "", "CONC1", 5m, 0m, Itens(produtoId, "1", "5")));
            responses.AddRange(res.Select(r => r.RawJson()));
        }
        catch (Exception ex)
        {
            responses.Add($"EXCECAO: {ex.GetType().Name}: {ex.Message}");
        }

        var (estoque, vendas, _) = Conferir(dbPath, produtoId);
        ctx1.Dispose();
        ctx2.Dispose();
        ApagarBanco(dbPath);

        Assert.True(estoque == 0m && vendas == 1,
            $"BUG CONFIRMADO (BUG-05, concorrencia): estoque inicial 1, duas vendas " +
            $"simultaneas de 1 un -> vendas gravadas={vendas}, estoque final={estoque}. " +
            $"Respostas: [{string.Join(" | ", responses)}]. Nao existe transacao, lock " +
            $"nem rechecagem de saldo: as duas requisicoes leem o mesmo estoque e o " +
            $"ultimo SaveChanges sobrescreve o do outro (perda de estoque / venda fantasma).");
    }
    // ---------------------------------------------------------------------
    // Estoque 2 + duas vendas concorrentes de 2 un: o comportamento CORRETO
    // e exatamente 1 venda aceita (a 2a deve ser barrada por "Estoque
    // insuficiente"), com estoque final 0 e nunca negativo.
    //
    // Nota: o antigo QA-V12b era um teste de CARACTERIZACAO que aceitava
    // qualquer desfecho ("estoque == 0"), usado so para documentar o bug.
    // Hoje o fluxo ja rejeita a venda excedente, entao ele virou um teste de
    // REGRESSAO: falha se as DUAS vendas forem aceitas (BUG-05) ou se o
    // estoque ficar negativo (dano contabil).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task VendasSimultaneas_NaoPodemGerarEstoqueNegativo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"qa-conc2-{Guid.NewGuid():N}.db");
        int produtoId = Semear(dbPath, "PRODUTO CONCORRENTE 2", "7891000000036", 2m, "CONC2");

        var ctx1 = NovoContexto(dbPath);
        var ctx2 = NovoContexto(dbPath);

        var respostas = new List<string>();
        try
        {
            var res = await Task.WhenAll(
                new CaixaController(ctx1).FinalizarVenda("Dinheiro", "", "CONC2", 10m, 0m, Itens(produtoId, "2", "5")),
                new CaixaController(ctx2).FinalizarVenda("Dinheiro", "", "CONC2", 10m, 0m, Itens(produtoId, "2", "5")));
            respostas.AddRange(res.Select(r => r.RawJson()));
        }
        catch (Exception ex)
        {
            respostas.Add($"EXCECAO: {ex.GetType().Name}: {ex.Message}");
        }

        var (estoque, vendas, totais) = Conferir(dbPath, produtoId);
        ctx1.Dispose();
        ctx2.Dispose();
        ApagarBanco(dbPath);

        // Correto: 1 venda, estoque 0 (nunca negativo).
        // BUG-05 reincidente: 2 vendas aceitas -> 4 unidades vendidas num estoque de 2.
        Assert.True(vendas == 1 && estoque == 0m,
            $"BUG CONFIRMADO (BUG-05, concorrencia): estoque inicial 2, duas vendas " +
            $"simultaneas de 2 un -> vendas gravadas={vendas}, estoque final={estoque}. " +
            $"Totais=[{string.Join(", ", totais)}]. Respostas=[{string.Join(" | ", respostas)}]. " +
            $"Esperado: 1 venda e estoque 0. Duas vendas aceitas = 4 unidades vendidas " +
            $"num estoque de 2; estoque negativo = dano contabil no inventario.");
    }
}
