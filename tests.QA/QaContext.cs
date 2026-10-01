using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// Base de todos os testes de CARACTERIZACAO.
///
/// IMPORTANTE: cria um banco SQLite ISOLADO em %TEMP% para cada teste.
/// O controleestoque.db versionado NUNCA e aberto, lido ou alterado por esta suite.
///
/// Padrao de uso:
///   using var qa = new QaContext();
///   var controller = new CaixaController(qa.Context);
///
/// Os testes devem FALHAR enquanto o bug existir e PASSAR quando ele for corrigido.
/// </summary>
public sealed class QaContext : IDisposable
{
    private readonly string _dbPath;

    public QaContext()
    {
        // Arquivo unico por instancia -> isolamento total entre testes.
        _dbPath = Path.Combine(Path.GetTempPath(), $"qa-teste-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        Context = new ApplicationDbContext(options);
        // EnsureCreated() apenas no banco TEMPORARIO de teste.
        // (A app de producao usa EnsureCreated no controleestoque.db - ver DadosQaTests.)
        Context.Database.EnsureCreated();
    }

    public ApplicationDbContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        try
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var f = _dbPath + suffix;
                if (File.Exists(f)) File.Delete(f);
            }
        }
        catch (IOException)
        {
            // Arquivo temporario pode estar bloqueado pelo SQLite; irrelevante para o teste.
        }
    }

    // ---------- helpers de seed ----------

    // unidade/permiteFracionado entraram junto com a generalizacao de medidas: antes
    // o teste so escolhia um local de guarda e o caixa decidia sozinho se aquilo era
    // fracionavel (era "Granel"). Agora o seed declara a medida, igual ao cadastro.
    public Produto SeedProduto(string nome = "PRODUTO TESTE", decimal preco = 10m,
                               decimal quantidade = 100m, TipoProduto tipo = TipoProduto.Prateleira,
                               string? codigoBarras = "7891000000001",
                               string unidade = "un", bool permiteFracionado = false)
    {
        var p = new Produto
        {
            Nome = nome,
            Preco = preco,
            Quantidade = quantidade,
            Tipo = tipo,
            CodigoBarras = codigoBarras,
            Unidade = unidade,
            PermiteFracionado = permiteFracionado
        };
        Context.Produtos.Add(p);
        Context.SaveChanges();
        return p;
    }

    public Vendedor SeedVendedor(string nome = "VENDEDOR TESTE", string codigo = "ABC123")
    {
        var v = new Vendedor { Nome = nome, Codigo = codigo, Ativo = true };
        Context.Vendedores.Add(v);
        Context.SaveChanges();
        return v;
    }

    public Venda SeedVenda(decimal total, string metodoPagamento = "Dinheiro",
                           DateTime? data = null, string? codigoVendedor = "ABC123",
                           bool isFechado = false)
    {
        var v = new Venda
        {
            DataVenda = data ?? DateTime.Now,
            MetodoPagamento = metodoPagamento,
            TotalVenda = total,
            ValorRecebido = total,
            ValorTroco = 0m,
            CodigoVendedor = codigoVendedor,
            IsFechado = isFechado,
            Itens = new List<VendaItem>()
        };
        Context.Vendas.Add(v);
        Context.SaveChanges();
        return v;
    }

    public CaixaMovimentacao SeedMovimentacao(string tipo, decimal valor, DateTime? data = null)
    {
        var m = new CaixaMovimentacao
        {
            Data = data ?? DateTime.Now,
            Tipo = tipo,
            Valor = valor,
            Descricao = tipo
        };
        Context.CaixaMovimentacoes.Add(m);
        Context.SaveChanges();
        return m;
    }
}
