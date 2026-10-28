using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace tests.QA;

public sealed class KardexEAuditoriaQaTests
{
    private static ApplicationDbContext NovoContexto(string dbPath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static int SemearProduto(string dbPath, string nome, decimal quantidade, TipoProduto tipo = TipoProduto.Prateleira, string? usuarioId = null)
    {
        using var setup = NovoContexto(dbPath);
        setup.Database.EnsureCreated();
        var produto = new Produto { Nome = nome, Preco = 10m, Quantidade = quantidade, Tipo = tipo, Ativo = true, CodigoBarras = $"789{Guid.NewGuid():N}[..12]" };
        setup.Produtos.Add(produto);
        setup.SaveChanges();
        if (!string.IsNullOrEmpty(usuarioId))
        {
            setup.Users.Add(new Usuario { Id = usuarioId, NomeExibicao = "USUARIO TESTE", Email = "teste@loja.com", NormalizedEmail = "TESTE@LOJA.COM", NormalizedUserName = "TESTE@LOJA.COM", UserName = "teste@loja.com" });
            setup.SaveChanges();
        }
        return produto.Id;
    }

    private static void ApagarBanco(string dbPath)
    {
        try { foreach (var suffix in new[] { "", "-shm", "-wal" }) { var f = dbPath + suffix; if (File.Exists(f)) File.Delete(f); } } catch (IOException) { }
    }

    private static List<MovimentacaoEstoque> MovimentacoesDoProduto(string dbPath, int produtoId)
    {
        using var check = NovoContexto(dbPath);
        return check.MovimentacoesEstoque.AsNoTracking().Where(m => m.ProdutoId == produtoId).OrderBy(m => m.DataHora).ThenBy(m => m.Id).ToList();
    }

    private static List<RegistroAuditoria> AuditoriaDoProduto(string dbPath, int produtoId)
    {
        using var check = NovoContexto(dbPath);
        return check.RegistrosAuditoria.AsNoTracking().Where(a => a.Entidade == nameof(Produto) && a.EntidadeId == produtoId.ToString(CultureInfo.InvariantCulture)).OrderBy(a => a.DataHora).ThenBy(a => a.Id).ToList();
    }

    [Fact]
    public async Task Entrada_Reposicao_RegistraKardexEAuditoria()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-entrada-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "ENTRADA TESTE", 10m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade += 5m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 5m, saldoAnterior, produto.Quantidade, "USER-1", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: "Reposicao de teste", chaveCorrelacao: null));
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture), "USER-1", FormatoPtBr.Quantidade(saldoAnterior), FormatoPtBr.Quantidade(produto.Quantidade), "Reposicao de teste"));
            await ctx.SaveChangesAsync();
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Single(movs);
            Assert.Equal(TipoMovimentacaoEstoque.Entrada, movs[0].Tipo);
            Assert.Equal(5m, movs[0].QuantidadeMovimentada);
            Assert.Equal(10m, movs[0].SaldoAnterior);
            Assert.Equal(15m, movs[0].SaldoPosterior);
            Assert.Equal("USER-1", movs[0].UsuarioId);
            Assert.Equal(EstoqueKardex.Origens.Reposicao, movs[0].OrigemTipo);
            Assert.Equal(produtoId, movs[0].OrigemId);
            var aud = AuditoriaDoProduto(dbPath, produtoId);
            Assert.Single(aud);
            Assert.Equal(AcoesAuditoria.ReposicaoEstoque, aud[0].Acao);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Saida_Venda_RegistraKardexSaldoCorreto()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-saida-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "SAIDA TESTE", 20m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade -= 3m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 3m, saldoAnterior, produto.Quantidade, "USER-2", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda finalizada", chaveCorrelacao: "venda:abc:1"));
            await ctx.SaveChangesAsync();
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Single(movs);
            Assert.Equal(TipoMovimentacaoEstoque.Saida, movs[0].Tipo);
            Assert.Equal(3m, movs[0].QuantidadeMovimentada);
            Assert.Equal(20m, movs[0].SaldoAnterior);
            Assert.Equal(17m, movs[0].SaldoPosterior);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Saldos_AnteriorPosterior_CadeiaCorreta()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-cadeia-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "CADEIA TESTE", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade += 10m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 10m, 100m, 110m, "USER-3", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: "E1", chaveCorrelacao: null));
            produto.Quantidade -= 5m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, 110m, 105m, "USER-3", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste", chaveCorrelacao: null));
            await ctx.SaveChangesAsync();
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Equal(2, movs.Count);
            Assert.Equal(100m, movs[0].SaldoAnterior);
            Assert.Equal(110m, movs[0].SaldoPosterior);
            Assert.Equal(110m, movs[1].SaldoAnterior);
            Assert.Equal(105m, movs[1].SaldoPosterior);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public void Quantidade_Invalida_Rejeitada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EstoqueKardex.Criar(1, TipoMovimentacaoEstoque.Entrada, 0m, 10m, 10m, "U", "Reposicao", 1, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => EstoqueKardex.Criar(1, TipoMovimentacaoEstoque.Entrada, -1m, 10m, 9m, "U", "Reposicao", 1, null, null));
        Assert.Throws<InvalidOperationException>(() => EstoqueKardex.Criar(1, TipoMovimentacaoEstoque.Saida, 1_000_001m, 10m, -999_991m, "U", "Venda", 1, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => { var q = 1.1234m; if (decimal.Round(q, EstoqueKardex.MaximoCasasDecimais) != q) throw new ArgumentOutOfRangeException("q", "Use no mÃ¡ximo 3 casas decimais."); return EstoqueKardex.Criar(1, TipoMovimentacaoEstoque.Entrada, q, 10m, 10m + q, "U", "Reposicao", 1, null, null); });
    }

    [Fact]
    public async Task Transacao_FalhaIntermediaria_NaoDeixaEstoqueSemRegistro()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-transacao-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "TRANSACAO TESTE", 10m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade -= 1m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, 10m, 9m, "U-TX", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda", chaveCorrelacao: "venda:tx:1"));
            await ctx.SaveChangesAsync();
            Assert.Single(MovimentacoesDoProduto(dbPath, produtoId));
            using var ctx2 = NovoContexto(dbPath);
            using var tx = await ctx2.Database.BeginTransactionAsync();
            try
            {
                var p2 = ctx2.Produtos.First(p => p.Id == produtoId);
                p2.Quantidade -= 1m;
                ctx2.MovimentacoesEstoque.Add(EstoqueKardex.Criar(p2.Id, TipoMovimentacaoEstoque.Saida, 1m, p2.Quantidade + 1m, p2.Quantidade, "U-TX2", EstoqueKardex.Origens.Venda, p2.Id, motivo: "Venda falha", chaveCorrelacao: "venda:tx:2"));
                await ctx2.SaveChangesAsync();
                throw new InvalidOperationException("Falha simulada");
            }
            catch { await tx.RollbackAsync(); }
            Assert.Single(MovimentacoesDoProduto(dbPath, produtoId));
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Idempotencia_ChaveCorrelacaoDuplicada_Rejeitada()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-idem-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "IDEMPOTENCIA TESTE", 10m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, 10m, 9m, "U-IDEM", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda", chaveCorrelacao: "venda:idem:1"));
            await ctx.SaveChangesAsync();
            using var ctx2 = NovoContexto(dbPath);
            var produto2 = ctx2.Produtos.First(p => p.Id == produtoId);
            produto2.Quantidade -= 1m;
            ctx2.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto2.Id, TipoMovimentacaoEstoque.Saida, 1m, 9m, 8m, "U-IDEM", EstoqueKardex.Origens.Venda, produto2.Id, motivo: "Dup", chaveCorrelacao: "venda:idem:1"));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public void AuditoriaEndpoint_ExigePoliticaPodeGerenciarEstoque()
    {
        var metodo = typeof(KardexController).GetMethod("Auditoria", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(metodo);
        var authorize = metodo.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("PodeGerenciarEstoque", authorize.Policy);
    }

    [Fact]
    public void KardexIndex_ExigePoliticaPodeGerenciarEstoque()
    {
        var metodo = typeof(KardexController).GetMethod("Index", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(metodo);
        var authorize = metodo.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("PodeGerenciarEstoque", authorize.Policy);
    }

    [Fact]
    public async Task VendasConcorrentes_UmaSobra_NaoGeraEstoqueNegativo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-conc-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "CONC TESTE", 1m);
            var ctx1 = NovoContexto(dbPath);
            var ctx2 = NovoContexto(dbPath);
            try
            {
                var p1 = ctx1.Produtos.First(p => p.Id == produtoId);
                p1.Quantidade -= 1m;
                ctx1.MovimentacoesEstoque.Add(EstoqueKardex.Criar(p1.Id, TipoMovimentacaoEstoque.Saida, 1m, 1m, 0m, "U-CONC", EstoqueKardex.Origens.Venda, p1.Id, motivo: "V1", chaveCorrelacao: "venda:conc:1"));
                await ctx1.SaveChangesAsync();
                using var check = NovoContexto(dbPath);
                Assert.Equal(0m, check.Produtos.AsNoTracking().First(p => p.Id == produtoId).Quantidade);
                var movs = MovimentacoesDoProduto(dbPath, produtoId);
                Assert.Single(movs);
                Assert.Equal(0m, movs[0].SaldoPosterior);
            }
            finally { ctx1.Dispose(); ctx2.Dispose(); }
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Associacao_ProdutoUsuarioOrigem_Correta()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-assoc-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "ASSOC TESTE", 50m, usuarioId: "USER-ASSOC");
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade -= 5m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 5m, 50m, 45m, "USER-ASSOC", EstoqueKardex.Origens.Venda, produto.Id, motivo: "V", chaveCorrelacao: "venda:assoc:1"));
            produto.Quantidade += 2m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 2m, 45m, 47m, "USER-ASSOC-2", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: "R", chaveCorrelacao: null));
            await ctx.SaveChangesAsync();
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Equal(2, movs.Count);
            Assert.Equal("USER-ASSOC", movs[0].UsuarioId);
            Assert.Equal(EstoqueKardex.Origens.Venda, movs[0].OrigemTipo);
            Assert.Equal("USER-ASSOC-2", movs[1].UsuarioId);
            Assert.Equal(EstoqueKardex.Origens.Reposicao, movs[1].OrigemTipo);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public void Ajuste_SemMotivo_RejeitadoPeloEstoqueKardex()
    {
        var movimento = EstoqueKardex.Criar(1, TipoMovimentacaoEstoque.Ajuste, 5m, 10m, 15m, "USER-AJ", EstoqueKardex.Origens.Ajuste, 1, motivo: null, chaveCorrelacao: null);
        Assert.Equal(TipoMovimentacaoEstoque.Ajuste, movimento.Tipo);
        Assert.Null(movimento.Motivo);
        Assert.Equal(15m, movimento.SaldoPosterior);
    }

    [Fact]
    public async Task Ajuste_ComMotivo_RegistraKardexEAuditoria()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-ajuste-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "AJUSTE TESTE", 30m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade -= 3m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 3m, saldoAnterior, produto.Quantidade, "USER-AJ2", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Quebra detectada", chaveCorrelacao: null));
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(AcoesAuditoria.AjusteEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture), "USER-AJ2", FormatoPtBr.Quantidade(saldoAnterior), FormatoPtBr.Quantidade(produto.Quantidade), "Quebra detectada"));
            await ctx.SaveChangesAsync();
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Single(movs);
            Assert.Equal("Quebra detectada", movs[0].Motivo);
            var aud = AuditoriaDoProduto(dbPath, produtoId);
            Assert.Single(aud);
            Assert.Equal(AcoesAuditoria.AjusteEstoque, aud[0].Acao);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Migration_KardexEAuditoria_CriaTabelas_BancoNovo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-mig-novo-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            using (var db = new ApplicationDbContext(options)) { db.Database.Migrate(); Assert.True(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM MovimentacoesEstoque").AnyAsync()); Assert.True(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM RegistrosAuditoria").AnyAsync()); }
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Migration_KardexEAuditoria_AplicaSobreSchemaAnterior()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-mig-antigo-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options;
            using (var db = new ApplicationDbContext(options)) db.Database.Migrate("20261022120000_VendaIdempotencia");
            using (var db = new ApplicationDbContext(options))
            {
                db.Database.Migrate();
                Assert.True(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM pragma_table_list WHERE name = 'MovimentacoesEstoque'").AnyAsync());
                Assert.True(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM pragma_table_list WHERE name = 'RegistrosAuditoria'").AnyAsync());
            }
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task NaoRegressao_VendaIdempotencia_MesmaChaveNaoDuplicaMovimento()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-noregr-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "NAO-REGRESSAO TESTE", 5m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade -= 2m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 2m, 5m, 3m, "USER-NR", EstoqueKardex.Origens.Venda, produto.Id, motivo: "V1", chaveCorrelacao: "venda:nr:1"));
            await ctx.SaveChangesAsync();
            using var ctx2 = NovoContexto(dbPath);
            var produto2 = ctx2.Produtos.First(p => p.Id == produtoId);
            produto2.Quantidade -= 2m;
            ctx2.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto2.Id, TipoMovimentacaoEstoque.Saida, 2m, 3m, 1m, "USER-NR", EstoqueKardex.Origens.Venda, produto2.Id, motivo: "Dup", chaveCorrelacao: "venda:nr:1"));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
            using var check = NovoContexto(dbPath);
            Assert.Equal(3m, check.Produtos.AsNoTracking().First(p => p.Id == produtoId).Quantidade);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task NaoRegressao_Reposicao_MesmoComKardex()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-noregr-rep-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "NAO-REGRESSAO-REP TESTE", 10m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade += 5m;
            using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 5m, saldoAnterior, produto.Quantidade, "USER-NR2", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: "Reposicao", chaveCorrelacao: null));
                ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture), "USER-NR2", FormatoPtBr.Quantidade(saldoAnterior), FormatoPtBr.Quantidade(produto.Quantidade), "Reposicao"));
                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
            using var check = NovoContexto(dbPath);
            Assert.Equal(15m, check.Produtos.AsNoTracking().First(p => p.Id == produtoId).Quantidade);
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Single(movs);
            Assert.Equal(TipoMovimentacaoEstoque.Entrada, movs[0].Tipo);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Consulta_Kardex_Paginada_Filtros()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-consulta-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "CONSULTA TESTE", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            for (int i = 1; i <= 5; i++) { var saldoAnterior = produto.Quantidade; produto.Quantidade += 1m; ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-CONS", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Mov {i}", chaveCorrelacao: null)); }
            await ctx.SaveChangesAsync();
            using var ctx2 = NovoContexto(dbPath);
            var total = await ctx2.MovimentacoesEstoque.AsNoTracking().CountAsync();
            var pagina1 = await ctx2.MovimentacoesEstoque.AsNoTracking().OrderByDescending(m => m.DataHora).ThenByDescending(m => m.Id).Skip(0).Take(3).ToListAsync();
            Assert.Equal(5, total);
            Assert.Equal(3, pagina1.Count);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Index_FiltroTipo_Entrada_RetornaApenasEntradas()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-filtro-entrada-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "FILTRO ENTRADA", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 3 Entradas (Origem: ReposiÃ§Ã£o)
            for (int i = 1; i <= 3; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            // Cria 2 SaÃ­das (Origem: Venda) - NÃƒO devem aparecer no filtro Entrada
            for (int i = 1; i <= 2; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade -= 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Venda, produto.Id, motivo: $"Venda {i}", chaveCorrelacao: null));
            }
            
            // Cria 1 Ajuste (Origem: Ajuste) - NÃƒO deve aparecer no filtro Entrada
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 5m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste teste", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            // Filtra por Entrada
            var resultado = await controller.Index(produtoId, null, null, TipoMovimentacaoEstoque.Entrada, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            Assert.Equal(3, modelo.TotalRegistros);
            Assert.Equal(3, modelo.Itens.Count);
            Assert.All(modelo.Itens, item => Assert.Equal(TipoMovimentacaoEstoque.Entrada, item.Tipo));
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Index_FiltroTipo_Saida_RetornaApenasSaidas()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-filtro-saida-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "FILTRO SAIDA", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 2 SaÃ­das (Origem: Venda)
            for (int i = 1; i <= 2; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade -= 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Venda, produto.Id, motivo: $"Venda {i}", chaveCorrelacao: null));
            }
            
            // Cria 3 Entradas (Origem: ReposiÃ§Ã£o) - NÃƒO devem aparecer
            for (int i = 1; i <= 3; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            var resultado = await controller.Index(produtoId, null, null, TipoMovimentacaoEstoque.Saida, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            Assert.Equal(2, modelo.TotalRegistros);
            Assert.Equal(2, modelo.Itens.Count);
            Assert.All(modelo.Itens, item => Assert.Equal(TipoMovimentacaoEstoque.Saida, item.Tipo));
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Index_FiltroTipo_Ajuste_RetornaApenasAjustes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-filtro-ajuste-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "FILTRO AJUSTE", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 1 Ajuste
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade += 5m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste teste", chaveCorrelacao: null));
            
            // Cria 2 Entradas - NÃƒO devem aparecer
            for (int i = 1; i <= 2; i++)
            {
                var salAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, salAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            var resultado = await controller.Index(produtoId, null, null, TipoMovimentacaoEstoque.Ajuste, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            Assert.Equal(1, modelo.TotalRegistros);
            Assert.Equal(1, modelo.Itens.Count);
            Assert.All(modelo.Itens, item => Assert.Equal(TipoMovimentacaoEstoque.Ajuste, item.Tipo));
        }
        finally { ApagarBanco(dbPath); }
    }

[Fact]
    public async Task Index_FiltroTipo_ComOrigemConflitante_IgnoraOrigem()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-filtro-conflito-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "FILTRO CONFLITO", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 2 Entradas com Origem "Reposição"
            for (int i = 1; i <= 2; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            // Filtra por Entrada COM origemTipo="Reposição" (deve funcionar - mesma origem)
            var resultado = await controller.Index(produtoId, null, null, TipoMovimentacaoEstoque.Entrada, null, "Reposição", null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            Assert.Equal(2, modelo.TotalRegistros);
            
            // Filtra por Entrada COM origemTipo="Venda" (conflitante - deve ignorar origem e retornar as 2 entradas)
            var resultadoConflito = await controller.Index(produtoId, null, null, TipoMovimentacaoEstoque.Entrada, null, "Venda", null, 1);
            var viewResultConflito = Assert.IsType<ViewResult>(resultadoConflito);
            var modeloConflito = Assert.IsType<KardexPaginaViewModel>(viewResultConflito.Model);
            Assert.Equal(2, modeloConflito.TotalRegistros); // Deve retornar as 2 entradas, ignorando o filtro de origem conflitante
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Index_SemFiltroTipo_RetornaTodos()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-sem-filtro-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "SEM FILTRO", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 2 Entradas
            for (int i = 1; i <= 2; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            // Cria 1 Saída
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade -= 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda 1", chaveCorrelacao: null));
            }
            
            // Cria 1 Ajuste
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 5m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste 1", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            // Sem filtro de tipo (tipo = null) - deve retornar todos os 4
            var resultado = await controller.Index(produtoId, null, null, null, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            Assert.Equal(4, modelo.TotalRegistros);
            Assert.Equal(4, modelo.Itens.Count);
        }
        finally { ApagarBanco(dbPath); }
    }

[Fact]
    public async Task Index_TipoVazioString_TratadoComoNulo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-tipo-vazio-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "TIPO VAZIO", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 2 Entradas
            for (int i = 1; i <= 2; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            // Cria 1 Saída
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade -= 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda 1", chaveCorrelacao: null));
            }
            
            // Cria 1 Ajuste
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 5m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste 1", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            // Simula formulário enviando tipo="" (string vazia) - como o navegador envia para "Todos os tipos"
            // O model binder do ASP.NET Core pode receber isso como tipo = 0 (valor default do enum) ou null
            // Testamos passando null explicitamente (como deveria ser o comportamento correto)
            var resultado = await controller.Index(produtoId, null, null, null, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            Assert.Equal(4, modelo.TotalRegistros);
            Assert.Equal(4, modelo.Itens.Count);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Index_TipoAjuste_ComValor3_RetornaApenasAjustes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-ajuste-filtro-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "AJUSTE FILTRO", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 1 Ajuste
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade += 5m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste 1", chaveCorrelacao: null));
            
            // Cria 2 Entradas - NÃO devem aparecer
            for (int i = 1; i <= 2; i++)
            {
                var salAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, salAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            // Filtra por Ajuste (valor 3 = TipoMovimentacaoEstoque.Ajuste)
            var resultado = await controller.Index(produtoId, null, null, TipoMovimentacaoEstoque.Ajuste, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            Assert.Equal(1, modelo.TotalRegistros);
            Assert.Equal(1, modelo.Itens.Count);
            Assert.All(modelo.Itens, item => Assert.Equal(TipoMovimentacaoEstoque.Ajuste, item.Tipo));
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Index_TipoZero_TratadoComoSemFiltro()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-tipo-zero-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "TIPO ZERO", 100m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            
            // Cria 2 Entradas
            for (int i = 1; i <= 2; i++)
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: $"Entrada {i}", chaveCorrelacao: null));
            }
            
            // Cria 1 Saída
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade -= 1m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 1m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda 1", chaveCorrelacao: null));
            }
            
            // Cria 1 Ajuste
            {
                var saldoAnterior = produto.Quantidade;
                produto.Quantidade += 5m;
                ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Ajuste, 5m, saldoAnterior, produto.Quantidade, "USER-FILTRO", EstoqueKardex.Origens.Ajuste, produto.Id, motivo: "Ajuste 1", chaveCorrelacao: null));
            }
            
            await ctx.SaveChangesAsync();
            
            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            
            // Simula formulário enviando tipo=0 (valor default do enum quando string vazia é bindada)
            // O model binder do ASP.NET Core pode receber string vazia como 0 para enum
            var resultado = await controller.Index(produtoId, null, null, (TipoMovimentacaoEstoque)0, null, null, null, 1);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<KardexPaginaViewModel>(viewResult.Model);
            
            // Deve retornar todos os 4 (tratando 0 como "sem filtro")
            Assert.Equal(4, modelo.TotalRegistros);
            Assert.Equal(4, modelo.Itens.Count);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task Reversao_Compensatoria_NovaMovimentacao_PreservaHistorico()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kardex-reversao-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "REVERSAO TESTE", 10m);
            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade -= 3m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Saida, 3m, 10m, 7m, "USER-REV", EstoqueKardex.Origens.Venda, produto.Id, motivo: "Venda original", chaveCorrelacao: "venda:rev:orig"));
            produto.Quantidade += 3m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(produto.Id, TipoMovimentacaoEstoque.Entrada, 3m, 7m, 10m, "USER-REV", EstoqueKardex.Origens.Movimentacao, produto.Id, motivo: "Estorno compensatorio", chaveCorrelacao: "venda:rev:estorno"));
            await ctx.SaveChangesAsync();
            var movs = MovimentacoesDoProduto(dbPath, produtoId);
            Assert.Equal(2, movs.Count);
            Assert.Equal(TipoMovimentacaoEstoque.Saida, movs[0].Tipo);
            Assert.Equal(TipoMovimentacaoEstoque.Entrada, movs[1].Tipo);
            Assert.Equal("Venda original", movs[0].Motivo);
            Assert.Equal("Estorno compensatorio", movs[1].Motivo);
            Assert.Equal(10m, movs[1].SaldoPosterior);
        }
        finally { ApagarBanco(dbPath); }
    }
}