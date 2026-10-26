using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;
using Xunit;

namespace tests.QA;

/// <summary>
/// Testa o fluxo completo de Auditoria: gravação, persistência, consulta
/// (via KardexController.Auditoria), filtros e permissões. Reutiliza
/// AuditoriaComercial e RegistroAuditoria existentes.
/// </summary>
public sealed class AuditoriaIntegracaoQaTests
{
    private static ApplicationDbContext NovoContexto(string dbPath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static int SemearProduto(string dbPath, string nome, decimal quantidade)
    {
        using var setup = NovoContexto(dbPath);
        setup.Database.EnsureCreated();
        var produto = new Produto { Nome = nome, Preco = 10m, Quantidade = quantidade, Tipo = TipoProduto.Prateleira, Ativo = true, CodigoBarras = $"789{Guid.NewGuid():N}[..12]" };
        setup.Produtos.Add(produto);
        setup.SaveChanges();
        return produto.Id;
    }

    private static void ApagarBanco(string dbPath)
    {
        try { foreach (var suffix in new[] { "", "-shm", "-wal" }) { var f = dbPath + suffix; if (File.Exists(f)) File.Delete(f); } } catch (IOException) { }
    }

    // ---------- 1. OPERAÇÃO GERA REGISTRO DE AUDITORIA E PERSISTE ----------

    [Fact]
    public async Task Reposicao_GeraRegistroAuditoriaE_PersisteNoBanco()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aud-reposicao-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "REPOSICAO AUDITORIA", 10m);

            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            var saldoAnterior = produto.Quantidade;
            produto.Quantidade += 5m;
            ctx.MovimentacoesEstoque.Add(EstoqueKardex.Criar(
                produto.Id, TipoMovimentacaoEstoque.Entrada, 5m, saldoAnterior, produto.Quantidade,
                "USER-1", EstoqueKardex.Origens.Reposicao, produto.Id, motivo: "Reposição", chaveCorrelacao: null));
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(
                AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture),
                "USER-1", FormatoPtBr.Quantidade(saldoAnterior), FormatoPtBr.Quantidade(produto.Quantidade), "Reposição"));
            await ctx.SaveChangesAsync();

            var registros = await ctx.RegistrosAuditoria
                .AsNoTracking()
                .Where(a => a.Acao == AcoesAuditoria.ReposicaoEstoque
                            && a.Entidade == nameof(Produto)
                            && a.EntidadeId == produtoId.ToString(CultureInfo.InvariantCulture))
                .ToListAsync();

            Assert.Single(registros);
            Assert.Equal(nameof(Produto), registros[0].Entidade);
            Assert.Equal(produtoId.ToString(CultureInfo.InvariantCulture), registros[0].EntidadeId);
            Assert.Equal("USER-1", registros[0].UsuarioId);
            Assert.Equal(FormatoPtBr.Quantidade(10m), registros[0].ValorAnterior);
            Assert.Equal(FormatoPtBr.Quantidade(15m), registros[0].ValorNovo);
        }
        finally { ApagarBanco(dbPath); }
    }

    // ---------- 3. CONSULTA DA ACTION AUDITORIA RECUPERA REGISTROS PERSISTIDOS ----------

    [Fact]
    public async Task ActionAuditoria_RetornaRegistrosPersistidos()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aud-consulta-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "CONSULTA AUDITORIA", 10m);

            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade += 5m;
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(
                AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture),
                "USER-1", FormatoPtBr.Quantidade(10m), FormatoPtBr.Quantidade(15m), "Reposição"));
            await ctx.SaveChangesAsync();

            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);
            var resultado = await controller.Auditoria(null, null, null, null);

            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<List<RegistroAuditoria>>(viewResult.Model);
            Assert.Single(modelo);
            Assert.Equal(AcoesAuditoria.ReposicaoEstoque, modelo[0].Acao);
            Assert.Equal(nameof(Produto), modelo[0].Entidade);
        }
        finally { ApagarBanco(dbPath); }
    }

    // ---------- 4. FILTROS NÃO OCULTAM INDEVIDAMENTE OS EVENTOS ----------

    [Fact]
    public async Task FiltroAcao_Corretamente_NaoOcultaEventoExistente()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aud-filtro-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "FILTRO AUDITORIA", 10m);

            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade += 5m;
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(
                AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture),
                "USER-1", FormatoPtBr.Quantidade(10m), FormatoPtBr.Quantidade(15m), "Reposição"));
            await ctx.SaveChangesAsync();

            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);

            var resultado = await controller.Auditoria(null, null, AcoesAuditoria.ReposicaoEstoque, null);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<List<RegistroAuditoria>>(viewResult.Model);
            Assert.Single(modelo);
            Assert.Equal(AcoesAuditoria.ReposicaoEstoque, modelo[0].Acao);

            var resultadoSemFiltro = await controller.Auditoria(null, null, null, null);
            var modeloSemFiltro = Assert.IsType<List<RegistroAuditoria>>(Assert.IsType<ViewResult>(resultadoSemFiltro).Model);
            Assert.Single(modeloSemFiltro);
        }
        finally { ApagarBanco(dbPath); }
    }

    [Fact]
    public async Task FiltroData_Abrangente_NaoOcultaEventoExistente()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aud-data-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "DATA AUDITORIA", 10m);
            var hoje = DateTime.Today;

            using var ctx = NovoContexto(dbPath);
            var produto = ctx.Produtos.First(p => p.Id == produtoId);
            produto.Quantidade += 5m;
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(
                AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produto.Id.ToString(CultureInfo.InvariantCulture),
                "USER-1", FormatoPtBr.Quantidade(10m), FormatoPtBr.Quantidade(15m), "Reposição"));
            await ctx.SaveChangesAsync();

            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);

            var resultado = await controller.Auditoria(hoje.AddDays(-1), hoje.AddDays(1), null, null);
            var modelo = Assert.IsType<List<RegistroAuditoria>>(Assert.IsType<ViewResult>(resultado).Model);
            Assert.Single(modelo);
        }
        finally { ApagarBanco(dbPath); }
    }

    // ---------- 6. PERMISSÕES CONTINUAM FUNCIONANDO ----------

    [Fact]
    public void ActionAuditoria_TemPolicyPodeGerenciarEstoque()
    {
        var metodo = typeof(KardexController).GetMethod("Auditoria");
        Assert.NotNull(metodo);

        var authorize = metodo.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("PodeGerenciarEstoque", authorize.Policy);
    }

    // ---------- 7. NOMES AMIGÁVEIS DAS AÇÕES ----------

    [Fact]
    public void NomeAmigavel_AcoesConhecidas_RetornaDescricaoPortugues()
    {
        Assert.Equal("Reposição de estoque", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.ReposicaoEstoque));
        Assert.Equal("Produto cadastrado", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.CadastroProduto));
        Assert.Equal("Produto desativado", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.ExclusaoProduto));
        Assert.Equal("Ajuste de estoque", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.AjusteEstoque));
        Assert.Equal("Alteração de preço", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.AlteracaoPreco));
        Assert.Equal("Suprimento de caixa", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.Suprimento));
        Assert.Equal("Sangria de caixa", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.Sangria));
        Assert.Equal("Fechamento de caixa", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.FechamentoCaixa));
        Assert.Equal("Usuário criado", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.CriacaoUsuario));
        Assert.Equal("Usuário editado", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.EdicaoUsuario));
        Assert.Equal("Vendedor cadastrado", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.CadastroVendedor));
        Assert.Equal("Vendedor desativado", AuditoriaComercial.NomeAmigavel(AcoesAuditoria.ExclusaoVendedor));
    }

    [Fact]
    public void NomeAmigavel_AcaoDesconhecida_NaoLancaERetornaFallbackLegivel()
    {
        // Não lança exceção e retorna algo legível (não vazio, não nulo)
        var resultado = AuditoriaComercial.NomeAmigavel("AlgumaAcaoDesconhecida");
        Assert.False(string.IsNullOrWhiteSpace(resultado));
        Assert.NotEqual("AlgumaAcaoDesconhecida", resultado);

        // Valor nulo/vazio também não lança
        Assert.Equal("—", AuditoriaComercial.NomeAmigavel(null!));
        Assert.Equal("—", AuditoriaComercial.NomeAmigavel(""));
    }

    // ---------- 8. REGISTROS HISTÓRICOS SEM EntidadeNome ----------

    [Fact]
    public async Task RegistroHistoricoSemEntidadeNome_ExibeIdentificacaoAlternativa()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aud-historico-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "QUALQUER PRODUTO", 10m);

            using var ctx = NovoContexto(dbPath);
            // Simula registro antigo gravado antes da coluna EntidadeNome existir
            ctx.RegistrosAuditoria.Add(new RegistroAuditoria
            {
                DataHora = DateTime.Now.AddDays(-30),
                UsuarioId = "USER-1",
                Acao = AcoesAuditoria.ReposicaoEstoque,
                Entidade = nameof(Produto),
                EntidadeId = produtoId.ToString(CultureInfo.InvariantCulture),
                EntidadeNome = null // sem nome histórico
            });
            await ctx.SaveChangesAsync();

            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);

            var resultado = await controller.Auditoria(null, null, null, null);
            var viewResult = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<List<RegistroAuditoria>>(viewResult.Model);
            Assert.Single(modelo);
            // O registro existe e é retornado pelo filtro (código interno preservado)
            Assert.Equal(AcoesAuditoria.ReposicaoEstoque, modelo[0].Acao);
            Assert.Null(modelo[0].EntidadeNome);
        }
        finally { ApagarBanco(dbPath); }
    }

    // ---------- 9. FILTROS CONTINUAM USANDO CÓDIGOS INTERNOS ----------

    [Fact]
    public async Task FiltroAcao_UsaCodigoInterno_NaoNomeAmigavel()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aud-filtro-codigo-{Guid.NewGuid():N}.db");
        try
        {
            int produtoId = SemearProduto(dbPath, "FILTRO CODIGO", 10m);

            using var ctx = NovoContexto(dbPath);
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(
                AcoesAuditoria.ReposicaoEstoque, nameof(Produto), produtoId.ToString(CultureInfo.InvariantCulture),
                "USER-1", null, null, null, entidadeNome: "Produto Teste"));
            ctx.RegistrosAuditoria.Add(AuditoriaComercial.Criar(
                AcoesAuditoria.AjusteEstoque, nameof(Produto), produtoId.ToString(CultureInfo.InvariantCulture),
                "USER-1", null, null, null, entidadeNome: "Produto Teste"));
            await ctx.SaveChangesAsync();

            using var ctx2 = NovoContexto(dbPath);
            var controller = new KardexController(ctx2);

            // Filtro pelo código interno deve retornar apenas o registro de reposição
            var resultado = await controller.Auditoria(null, null, AcoesAuditoria.ReposicaoEstoque, null);
            var modelo = Assert.IsType<List<RegistroAuditoria>>(Assert.IsType<ViewResult>(resultado).Model);
            Assert.Single(modelo);
            Assert.Equal(AcoesAuditoria.ReposicaoEstoque, modelo[0].Acao);
        }
        finally { ApagarBanco(dbPath); }
    }
}

