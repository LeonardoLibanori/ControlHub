using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Vendedores / Cupom (CaixaController).
/// </summary>
public class VendedoresQaTests
{
    // CONTROLE: CadastrarVendedor rejeita codigo exatamente duplicado.
    [Fact]
    public async Task CadastrarVendedor_DeveRejeitarCupomDuplicado()
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        await controller.CadastrarVendedor("Maria", "CUPOM1");
        var segundo = await controller.CadastrarVendedor("Joao", "CUPOM1");

        Assert.False(segundo.IsSuccess());
        Assert.Equal(1, qa.Context.Vendedores.AsNoTracking().Count());
    }

    // ---------------------------------------------------------------------
    // BUG: case-sensitivity entre ValidarCupom e FinalizarVenda.
    // ValidarCupom compara com ToUpper() nos dois lados (case-INSENSITIVE):
    //     .AnyAsync(v => v.Codigo.ToUpper() == codigo.ToUpper() && v.Ativo)
    // FinalizarVenda compara direto:
    //     .FirstOrDefaultAsync(v => v.Codigo == codigoVendedor && v.Ativo)
    // No SQLite a comparacao "=" e case-SENSITIVE (BINARY collation).
    // Resultado: o cupom "CUPOM1" passa na validacao como "cupom1", mas a
    // venda e REJEITADA por "cupom nao encontrado" - o vendedor nao consegue
    // fechar a venda com o mesmo cupom que o sistema acabou de validar.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Cupom_DeveAceitarMesmoCodigoEmCaixaDiferente_NasDuasEtapas()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(nome: "Maria", codigo: "CUPOM1");
        var produto = qa.SeedProduto(preco: 10m, quantidade: 100m);

        var controller = new CaixaController(qa.Context);

        // 1) Validacao do cupom em minuscula (como o usuario digitaria).
        var validacao = await controller.ValidarCupom("cupom1");
        Assert.True(validacao.GetProp("exists") is true,
            "ValidarCupom deveria aceitar o cupom em caixa diferente.");

        // 2) A venda usa exatamente o mesmo codigo validado.
        var venda = await controller.FinalizarVenda("Dinheiro", null, "cupom1", 0m, 0m,
            new List<CaixaController.ItemVendaSimples>
            {
                new() { ProdutoId = produto.Id.ToString(), Quantidade = "1", Preco = "0" }
            });

        Assert.True(venda.IsSuccess(),
            $"ValidarCupom aceitou 'cupom1' mas FinalizarVenda rejeitou: {venda.GetStr("message")}");
    }

    // CONTROLE: a validacao aceita o codigo exato.
    [Fact]
    public async Task ValidarCupom_DeveAceitarCodigoExato()
    {
        using var qa = new QaContext();
        qa.SeedVendedor(nome: "Maria", codigo: "CUPOM1");

        var controller = new CaixaController(qa.Context);
        var result = await controller.ValidarCupom("CUPOM1");

        Assert.True(result.GetProp("exists") is true);
    }

    // CONTROLE: vendedor desativado nao e aceito na validacao.
    [Fact]
    public async Task ValidarCupom_DeveRejeitarVendedorInativo()
    {
        using var qa = new QaContext();
        var vendedor = qa.SeedVendedor(codigo: "CUPOM1");
        vendedor.Ativo = false;
        qa.Context.SaveChanges();

        var controller = new CaixaController(qa.Context);
        var result = await controller.ValidarCupom("CUPOM1");

        Assert.True(result.GetProp("exists") is false);
    }

    // ---------------------------------------------------------------------
    // CONTROLE (comportamento desejado): a exclusao de vendedor e LOGICA
    // (Ativo = false), preservando o historico de vendas ja realizadas.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExcluirVendedor_DevePreservarHistoricoDeVendas()
    {
        using var qa = new QaContext();
        var vendedor = qa.SeedVendedor(codigo: "CUPOM1");
        qa.SeedVenda(50m, codigoVendedor: "CUPOM1");

        var controller = new CaixaController(qa.Context);
        var result = await controller.ExcluirVendedor("CUPOM1");

        Assert.True(result.IsSuccess());

        // O vendedor continua no banco (exclusao logica)...
        var salvo = qa.Context.Vendedores.AsNoTracking().Single(v => v.Id == vendedor.Id);
        Assert.False(salvo.Ativo);

        // ...e o historico de vendas permanece intacto.
        Assert.Equal(1, qa.Context.Vendas.AsNoTracking().Count());
    }

    // CONTROLE: apos desativar, o vendedor nao fecha mais vendas.
    [Fact]
    public async Task VendedorDesativado_DeveTerVendaRejeitada()
    {
        using var qa = new QaContext();
        var vendedor = qa.SeedVendedor(codigo: "CUPOM1");
        var produto = qa.SeedProduto(quantidade: 10m);

        var controller = new CaixaController(qa.Context);
        await controller.ExcluirVendedor("CUPOM1");

        var venda = await controller.FinalizarVenda("Dinheiro", null, "CUPOM1", 0m, 0m,
            new List<CaixaController.ItemVendaSimples>
            {
                new() { ProdutoId = produto.Id.ToString(), Quantidade = "1", Preco = "0" }
            });

        Assert.False(venda.IsSuccess());
        Assert.Empty(qa.Context.Vendas.AsNoTracking().ToList());
    }
}
