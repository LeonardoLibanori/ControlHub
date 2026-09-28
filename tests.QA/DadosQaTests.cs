using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Migrations;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Dados / esquema / persistencia.
/// </summary>
public class DadosQaTests
{
    // ---------------------------------------------------------------------
    // BUG: Program.cs usa EnsureCreated() em vez de Migrate().
    // EnsureCreated() cria o banco a partir do modelo ATUAL e nao aplica
    // migrations. Se a base ja existir (ou for criada antes de uma migration
    // nova), EnsureCreated NAO altera o esquema: as colunas adicionadas em
    // migrations posteriores nunca aparecem e a app quebra em runtime com
    // "no such column". O correto e Database.Migrate().
    // Este teste le o Program.cs e verifica o metodo chamado.
    // ---------------------------------------------------------------------
    [Fact]
    public void Program_DeveUsarMigrate_EmVezDeEnsureCreated()
    {
        var programCs = LocalizarArquivo("Program.cs");
        Assert.True(File.Exists(programCs), $"Program.cs nao encontrado em {programCs}");

        var texto = File.ReadAllText(programCs);

        Assert.DoesNotContain("EnsureCreated", texto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Migrate", texto, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // BUG: decimal persistido como TEXT no SQLite.
    // A migration Inicial_Nova mapeia todo decimal como type: "TEXT".
    // Consequencias: (1) ORDER BY passa a ordenar lexicograficamente
    // ("10,00" < "9,00" porque '1' < '9'), quebrando relatorios;
    // (2) SUM/aritmetica textual fragile; (3) consultas por intervalo erram.
    // ---------------------------------------------------------------------
    [Fact]
    public void Decimal_NaoDeveSerPersistidoComoText()
    {
        var migration = LocalizarArquivo(Path.Combine("Migrations", "20260220171624_Inicial_Nova.cs"));
        Assert.True(File.Exists(migration), $"Migration nao encontrada: {migration}");

        var texto = File.ReadAllText(migration);

        // Colunas decimais devem usar tipo numerico, nao TEXT.
        Assert.DoesNotMatch(@"table\.Column<decimal>\(type:\s*""TEXT""", texto);
    }

    // CONTROLE: o decimal deve ser lido de volta com precisao (usa o banco temporario).
    [Fact]
    public void Decimal_DeveSerLidoComPrecisaoAposGravacao()
    {
        using var qa = new QaContext();

        qa.SeedVenda(1234.56m, metodoPagamento: "Dinheiro");
        var venda = qa.Context.Vendas.AsNoTracking().Single();

        Assert.Equal(1234.56m, venda.TotalVenda);
    }

    // CONTROLE: ordenacao de decimais deve ser numerica, nao lexicografica.
    [Fact]
    public void Decimal_DeveOrdenarNumericamente()
    {
        using var qa = new QaContext();

        qa.SeedVenda(9.00m, metodoPagamento: "Dinheiro", codigoVendedor: "A");
        qa.SeedVenda(10.00m, metodoPagamento: "Dinheiro", codigoVendedor: "B");

        var ordenados = qa.Context.Vendas.AsNoTracking()
            .OrderByDescending(v => v.TotalVenda)
            .Select(v => v.TotalVenda)
            .ToList();

        Assert.Equal(10.00m, ordenados[0]);
        Assert.Equal(9.00m, ordenados[1]);
    }

    // CONTROLE: a soma de decimais deve ser exata.
    [Fact]
    public void Decimal_DeveSomarSemPerda()
    {
        using var qa = new QaContext();

        qa.SeedVenda(0.10m, metodoPagamento: "Dinheiro", codigoVendedor: "A");
        qa.SeedVenda(0.20m, metodoPagamento: "Dinheiro", codigoVendedor: "B");

        var total = qa.Context.Vendas.AsNoTracking().Sum(v => v.TotalVenda);
        Assert.Equal(0.30m, total);
    }

    // ---------------------------------------------------------------------
    // CONTROLE: o EnsureCreated() aqui e legitimo - roda no banco ISOLADO
    // do %TEMP% criado pelo QaContext, nunca no controleestoque.db do projeto.
    // ---------------------------------------------------------------------
    [Fact]
    public void BancoDeTeste_Isolado_DeveCriarEsquemaCompleto()
    {
        using var qa = new QaContext();

        // Todas as tabelas esperadas pelo app precisam existir.
        Assert.True(qa.Context.Database.CanConnect());

        var tabelas = new List<string>();
        var conexao = qa.Context.Database.GetDbConnection();
        conexao.Open();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tabelas.Add(reader.GetString(0));
        }
        conexao.Close();

        Assert.Contains("Produtos", tabelas);
        Assert.Contains("Vendas", tabelas);
        Assert.Contains("VendaItens", tabelas);
        Assert.Contains("Vendedores", tabelas);
        Assert.Contains("CaixaMovimentacoes", tabelas);
    }

    // Confirma que a suite NAO abre o controleestoque.db versionado.
    [Fact]
    public void Suite_NaoDeveAbrirOBancoVersionado()
    {
        var caminho = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrEmpty(caminho))
        {
            // Nenhuma connection string sobrescrita: cada teste cria a sua
            // em %TEMP%. Este e o comportamento esperado.
            Assert.True(true);
            return;
        }

        Assert.DoesNotContain("controleestoque.db", caminho, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sobe a arvore ate achar a raiz do projeto web (onde fica o .csproj).</summary>
    private static string LocalizarArquivo(string caminhoRelativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ControleEstoqueLoja.csproj")))
                return Path.Combine(dir.FullName, caminhoRelativo);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do projeto web nao encontrada a partir de " + AppContext.BaseDirectory);
    }
}
