using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// ETAPA A - o que acontece com <see cref="Produto.Tipo"/> quando o valor chega
/// da tela.
///
/// POR QUE EXISTE: o cadastro recebe `tipo` como TEXTO do formulario
/// (<c>HomeController.Novo(string nome, string tipo, ...)</c>) e converte com
/// <c>Enum.TryParse</c>. Esse Parse tem uma propriedade perigosa: "99" e "-5" NAO
/// sao texto invalido - sao numeros que ele aceita, devolvendo um valor que NAO
/// existe em TipoProduto. O produto passa a ser gravado com um Tipo fora do enum,
/// e ai o PDV (CaixaController) trata granel e prateleira igual, o Excel imprime
/// uma coluna que ninguem sabe ler e a migration 20261001120000, que filtra por
/// Tipo = 0, deixa de enxergar o produto.
///
/// ESTES TESTES DEVEM FALHAR enquanto a tela mandar "99"/"-5": e esse vermelho
/// que mostra o buraco antes da correcao. Nenhum teste existente foi alterado.
/// </summary>
public class ProdutoTipoQaTests
{
    // =====================================================================
    // (a) Tipo valido: o que a tela mandou e o que fica gravado
    // =====================================================================

    [Theory]
    [InlineData("PorPeso")]
    [InlineData("Prateleira")]
    public void Novo_DeveGravarOTipoEnviadoPelaTela(string tipo)
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context);

        controller.Novo("ARROZ", tipo, "10", "500", null);

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(tipo, salvo.Tipo.ToString());
    }

    // =====================================================================
    // (b) Tipo INVALIDO no cadastro: nunca gravar valor fora do enum
    // =====================================================================

    // "99" e "-5" sao o caso perigoso: Enum.TryParse aceita e devolve 99 / -5,
    // que NAO existem em TipoProduto. "abc", vazio e null caem no fallback.
    [Theory]
    [InlineData("99")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Novo_NaoDeveGravarTipoForaDoEnum_QuandoTipoForInvalido(string? tipo)
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context);

        controller.Novo("FEIJAO", tipo!, "10", "500", null);

        var salvo = qa.Context.Produtos.AsNoTracking().Single();

        // 1) o dado gravado TEM de existir no enum (o que falha hoje com 99/-5)
        Assert.True(Enum.IsDefined(typeof(TipoProduto), salvo.Tipo),
            $"O cadastro gravou o tipo {salvo.Tipo} (valor {(int)salvo.Tipo}), que nao existe em TipoProduto.");

        // 2) e tem de ser o fallback atual: Prateleira (unidade "un")
        Assert.Equal(TipoProduto.Prateleira, salvo.Tipo);
    }

    // =====================================================================
    // (c) Tipo INVALIDO na edicao: mantem o tipo que o produto ja tinha
    // =====================================================================

    [Theory]
    [InlineData("99")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Editar_DeveManterOTipoAtual_QuandoTipoForInvalido(string? tipo)
    {
        using var qa = new QaContext();
        // Comeca em PorPeso (0) de proposito: e o tipo que a edicao tem de
        // preservar quando a tela mandar um valor que nao existe.
        var produto = qa.SeedProduto(nome: "BANANA", tipo: TipoProduto.PorPeso);
        var controller = new HomeController(qa.Context);

        controller.Editar(produto.Id, "BANANA", tipo!, null, "3000");

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);

        Assert.True(Enum.IsDefined(typeof(TipoProduto), salvo.Tipo),
            $"A edicao gravou o tipo {salvo.Tipo} (valor {(int)salvo.Tipo}), que nao existe em TipoProduto.");
        Assert.Equal(TipoProduto.PorPeso, salvo.Tipo);
    }

    // =====================================================================
    // (d) Tipos LEGADOS: continuam aceitos (existem no enum e em producao)
    // =====================================================================

    [Theory]
    [InlineData("Geladeira")]
    [InlineData("Freezer")]
    public void Novo_DeveContinuarAceitandoTiposLegados(string tipo)
    {
        using var qa = new QaContext();
        var controller = new HomeController(qa.Context);

        controller.Novo("REFRIGERANTE", tipo, "5", "900", null);

        var salvo = qa.Context.Produtos.AsNoTracking().Single();
        Assert.Equal(tipo, salvo.Tipo.ToString());
    }

    [Theory]
    [InlineData("Geladeira")]
    [InlineData("Freezer")]
    public void Editar_DeveContinuarAceitandoTiposLegados(string tipo)
    {
        using var qa = new QaContext();
        var produto = qa.SeedProduto(nome: "SORVETE", tipo: TipoProduto.Prateleira);
        var controller = new HomeController(qa.Context);

        controller.Editar(produto.Id, "SORVETE", tipo, null, "1500");

        var salvo = qa.Context.Produtos.AsNoTracking().First(p => p.Id == produto.Id);
        Assert.Equal(tipo, salvo.Tipo.ToString());
    }
}