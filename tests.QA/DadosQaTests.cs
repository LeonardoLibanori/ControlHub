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
    // BUG-20: decimal persistido como TEXT no SQLite.
    // A migration Inicial_Nova mapeia todo decimal como type: "TEXT".
    // Consequencias: (1) ORDER BY passa a ordenar lexicograficamente
    // ("10,00" < "9,00" porque '1' < '9'), quebrando relatorios;
    // (2) SUM/aritmetica textual fragile; (3) consultas por intervalo erram.
    //
    // ONDE A ASSESSO INCIDE (reescrita em 30/09/2026, junto com a correcao).
    // A versao original procurava 'table.Column<decimal>(type: "TEXT")' dentro
    // de 20260220171624_Inicial_Nova.cs. Nao da para manter assim: migration ja
    // aplicada e o registro historico do que RODOU no banco da loja, e editar o
    // arquivo nao muda esquema nenhum - pior, criaria a armadilha de o
    // Inicial_Nova passar a declarar INTEGER enquanto a DecimalParaCentavos
    // continua multiplicando por 100 (o 999 de uma base nova viraria 99900).
    // A correcao e uma migration DE CONVERSAO por cima, que e o caminho normal
    // do EF, entao o que se mede e o ESQUEMA EFETIVO, em tres niveis:
    //   1. snapshot do modelo  -> o que o EF aplica em qualquer base nova;
    //   2. arquivo SQLite real -> depois de subir a cadeia inteira de migrations
    //      do zero, pelo mesmo Database.Migrate() que o Program.cs usa;
    //   3. o numero cru        -> '9.99' legado em TEXT vira 999 integer, e
    //      1234.56 gravado pelo EF sai 123456 integer do arquivo. Mede tambem o
    //      SINTOMA dos dois lados: a mesma ORDER BY que no legado devolve
    //      "10.00,9.99" (lexicografico) devolve "999,1000,123456" depois.
    // ---------------------------------------------------------------------

    /// <summary>As 7 colunas de dinheiro do esquema. Estoque nao entra: e decimal fracionario.</summary>
    private static readonly (string Tabela, string Coluna)[] ColunasDeDinheiro =
    {
        ("Produtos", "Preco"),
        ("Vendas", "TotalVenda"),
        ("Vendas", "ValorRecebido"),
        ("Vendas", "ValorTroco"),
        ("VendaItens", "PrecoUnitario"),
        ("CaixaMovimentacoes", "Valor"),
        ("FluxoCaixa", "Valor"),
    };

    [Fact]
    public void Decimal_NaoDeveSerPersistidoComoText()
    {
        // --- 1. MODELO EFETIVO: no snapshot o provedor do dinheiro e long (INTEGER).
        var snapshot = File.ReadAllText(
            LocalizarArquivo(Path.Combine("Migrations", "ApplicationDbContextModelSnapshot.cs")));

        foreach (var (_, coluna) in ColunasDeDinheiro)
        {
            Assert.Matches($@"b\.Property<long>\(""{coluna}""\)", snapshot);
            Assert.DoesNotMatch($@"b\.Property<decimal>\(""{coluna}""\)", snapshot);
        }

        // DIVIDA DOCUMENTADA (RELATORIO-QA.md > "Lacunas conhecidas"): Quantidade
        // (estoque fracionario) continua decimal/TEXT - nao e dinheiro e ficou de
        // fora da conversao de proposito: 1,235 kg nao tem centavo.
        // Se nascer coluna nova de dinheiro como decimal/TEXT, esta lista quebra.
        var aindaDecimais = System.Text.RegularExpressions.Regex
            .Matches(snapshot, @"b\.Property<decimal>\(""(\w+)""\)")
            .Select(m => m.Groups[1].Value)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Quantidade", "Quantidade" }, aindaDecimais);

        // --- 2 e 3. BANCO REAL criado pela cadeia de migrations, no %TEMP%.
        VerificarConversaoNoArquivoReal();
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

    /// <summary>
    /// Sobe uma base LEGADA (parada na migration anterior a conversao), escreve
    /// dinheiro como TEXT do jeito do Inicial_Nova, roda o Database.Migrate() do
    /// Program.cs e mede o numero cru no arquivo. Banco sempre em %TEMP%.
    /// </summary>
    private static void VerificarConversaoNoArquivoReal()
    {
        var db = Path.Combine(Path.GetTempPath(), $"qa-centavos-{Guid.NewGuid():N}.db");
        // Pooling=False: sem pool, o Dispose libera o handle do arquivo e o
        // delete do finally funciona (o padrao do QaContext e o mesmo).
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={db};Pooling=False")
            .Options;

        try
        {
            // 2a. base LEGADA: para uma migration antes da conversao -- exatamente
            //     a migration em que o controleestoque.exemplo.db versionado esta
            //     parado (lido na __EFMigrationsHistory dele: Inicial_Nova,
            //     AddValorRecebidoValorTroco e ProdutoAtivo_ExclusaoLogica).
            using (var ctx = new ApplicationDbContext(options))
                ctx.Database.Migrate("20260928234611_ProdutoAtivo_ExclusaoLogica_VendaItemRestrict");

            // 2b. linha LEGADA de verdade: dinheiro gravado como texto '9.99',
            //     no formato que o Inicial_Nova escrevia. As duas linhas abaixo
            //     existem para medir o SINTOMA no arquivo, nao so o tipo.
            using (var ctx = new ApplicationDbContext(options))
            {
                Executar(ctx, @"INSERT INTO Produtos (Id, Ativo, CodigoBarras, Nome, Preco, Quantidade, Tipo)
                                VALUES (1, 1, NULL, 'MEL LEGADO', '9.99', '-50.0', 1);");
                Executar(ctx, @"INSERT INTO Produtos (Id, Ativo, CodigoBarras, Nome, Preco, Quantidade, Tipo)
                                VALUES (2, 1, NULL, 'ARROZ LEGADO', '10.00', '5.0', 1);");
                Executar(ctx, @"INSERT INTO Vendas (Id, CPFCliente, CodigoVendedor, DataVenda, IsFechado,
                                                    MetodoPagamento, TotalVenda, ValorRecebido, ValorTroco)
                                VALUES (1, NULL, 'ABC123', '2026-09-29 10:00:00', 0, 'Dinheiro',
                                        '9.99', '20.0', '10.01');");

                // antes: exatamente o comportamento que o BUG-20 descreve
                Assert.Equal("text=9.99", Bruto(ctx,
                    "SELECT typeof(Preco) || '=' || Preco FROM Produtos WHERE Id = 1"));

                // O SINTOMA, medido no arquivo: ORDER BY lexicografico coloca o mais
                // barato (10.00) antes do mais caro (9.99) porque '1' < '9'.
                Assert.Equal("10.00,9.99", Bruto(ctx,
                    "SELECT group_concat(Preco, ',') FROM (SELECT Preco FROM Produtos ORDER BY Preco)"));
            }

            // 2c. aplica o resto da cadeia (Identity + DecimalParaCentavos).
            using (var ctx = new ApplicationDbContext(options))
                ctx.Database.Migrate();

            using (var ctx = new ApplicationDbContext(options))
            {
                // O dado legado foi convertido NO ARQUIVO, nao apenas no modelo.
                Assert.Equal("integer=999", Bruto(ctx,
                    "SELECT typeof(Preco) || '=' || Preco FROM Produtos WHERE Id = 1"));
                Assert.Equal("integer=999", Bruto(ctx,
                    "SELECT typeof(TotalVenda) || '=' || TotalVenda FROM Vendas WHERE Id = 1"));
                Assert.Equal("integer=1001", Bruto(ctx,
                    "SELECT typeof(ValorTroco) || '=' || ValorTroco FROM Vendas WHERE Id = 1"));

                // Nenhuma linha de dinheiro sobrou como TEXT (nem nula, nem zerada).
                Assert.Equal("Preco=0 | TotalVenda=0 | ValorTroco=0", Bruto(ctx, @"SELECT group_concat(nome || '=' || n, ' | ') FROM (
                        SELECT 'Preco' AS nome, COUNT(*) AS n FROM Produtos WHERE Preco IS NULL OR typeof(Preco) <> 'integer'
                        UNION ALL SELECT 'TotalVenda', COUNT(*) FROM Vendas WHERE TotalVenda IS NULL OR typeof(TotalVenda) <> 'integer'
                        UNION ALL SELECT 'ValorTroco', COUNT(*) FROM Vendas WHERE ValorTroco IS NULL OR typeof(ValorTroco) <> 'integer')"));

                // 3a. o modelo devolve decimal com precisao.
                Assert.Equal(9.99m, ctx.Produtos.AsNoTracking().Single(p => p.Id == 1).Preco);

                // 3b. e grava: 1234.56 no C# tem que sair 123456 do arquivo.
                ctx.Produtos.Add(new Produto
                {
                    Nome = "CENTAVOS",
                    Preco = 1234.56m,
                    Quantidade = 2m,
                    Tipo = TipoProduto.Prateleira,
                    CodigoBarras = null,
                });
                ctx.SaveChanges();

                Assert.Equal("integer=123456", Bruto(ctx,
                    "SELECT typeof(Preco) || '=' || Preco FROM Produtos WHERE Nome = 'CENTAVOS'"));

                // 3c. o SINTOMA ao contrario: a MESMA ORDER BY do bloco 2b agora e
                //     numerica - 9,99 < 10,00 < 1.234,56, a ordem do dinheiro.
                Assert.Equal("999,1000,123456", Bruto(ctx,
                    "SELECT group_concat(Preco, ',') FROM (SELECT Preco FROM Produtos ORDER BY Preco)"));
                Assert.Equal(new[] { 9.99m, 10.00m, 1234.56m },
                    ctx.Produtos.AsNoTracking().OrderBy(p => p.Preco).Select(p => p.Preco).ToArray());
            }
        }
        finally
        {
            foreach (var sufixo in new[] { "", "-shm", "-wal" })
            {
                var arquivo = db + sufixo;
                try
                {
                    if (File.Exists(arquivo)) File.Delete(arquivo);
                }
                catch (IOException)
                {
                    // Arquivo temporario pode estar bloqueado pelo SQLite;
                    // irrelevante para o teste - o que importa ja foi aferido.
                }
            }
        }
    }

    /// <summary>SQL cru na conexao do contexto - simula o legado que o EF de hoje nao saberia escrever.</summary>
    private static void Executar(ApplicationDbContext ctx, string sql)
    {
        var conexao = ctx.Database.GetDbConnection();
        conexao.Open();
        try
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        finally
        {
            conexao.Close();
        }
    }

    /// <summary>Le o valor cru do arquivo com o typeof() na frente, sem o EF no meio.</summary>
    private static string Bruto(ApplicationDbContext ctx, string sql)
    {
        var conexao = ctx.Database.GetDbConnection();
        conexao.Open();
        try
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar()?.ToString() ?? string.Empty;
        }
        finally
        {
            conexao.Close();
        }
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
