using System.Reflection;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Seguranca.
/// Devem FALHAR enquanto a vulnerabilidade existir.
/// </summary>
public class SegurancaQaTests
{
    // ---------------------------------------------------------------------
    // BUG: ausencia de autenticacao/autorizacao.
    // Nenhum controller tem [Authorize]. Qualquer pessoa com acesso a rede
    // (a loja nao tem firewall interno) acessa /Caixa, /Home/Index, exclui
    // produtos e registra sangrias. Qualquer endpoint administrativo
    // (ListarVendedores, RegistrarMovimentacao, ExcluirVendedor) fica publico.
    // ---------------------------------------------------------------------
    [Fact]
    public void Controllers_NaoDevemExporEndpointsSemAutorizacao()
    {
        var controllers = typeof(CaixaController).Assembly
            .GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(controllers);

        var semAuthorize = controllers
            .Where(t => t.GetCustomAttribute<AuthorizeAttribute>() is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(semAuthorize.Count == 0,
            "Controllers sem [Authorize]: " + string.Join(", ", semAuthorize));
    }

    // ---------------------------------------------------------------------
    // BUG: ausencia de protecao anti-forgery (CSRF).
    // POSTs que mudam estado (FinalizarVenda, RegistrarMovimentacao,
    // Excluir, CadastrarVendedor, ExcluirVendedor, Editar, Novo) nao exigem
    // token anti-forgery. Um site malicioso aberto no navegador do operador
    // consegue disparar uma sangria ou uma venda via requisicao automatica.
    // ---------------------------------------------------------------------
    [Fact]
    public void PostActions_NaoDevemFaltarAntiForgery()
    {
        var controllers = new[] { typeof(CaixaController), typeof(HomeController) };

        var semToken = new List<string>();

        foreach (var ctrl in controllers)
        {
            var actions = ctrl.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null)
                .Where(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is null);

            foreach (var a in actions)
                semToken.Add($"{ctrl.Name}.{a.Name}");
        }

        Assert.True(semToken.Count == 0,
            "POSTs sem [ValidateAntiForgeryToken]: " + string.Join(", ", semToken));
    }

    // ---------------------------------------------------------------------
    // BUG: XSS armazenado - o nome do produto vai cru para o innerHTML.
    // HomeController.Novo grava o nome sem sanitizar e admin-caixa.js faz:
    //     btn.innerHTML = `<strong>${nomeOriginal}</strong> ...`
    // (wwwroot/js/admin-caixa.js, linha ~438)
    // Um produto chamado "<img src=x onerror=alert(1)>" executa script no
    // navegador de quem busca no PDV. A view Razor escapa (@Model.Nome);
    // o JavaScript nao escapa.
    // ---------------------------------------------------------------------
    [Fact]
    public void NomeDeProduto_ComScript_DeveSerSanitizadoParaOInnerHTML()
    {
        const string nomeMalicioso = "<img src=x onerror=alert(1)>";

        // 1) O cadastro deve rejeitar/sanitizar o nome.
        using (var qa = new QaContext())
        {
            var controller = new HomeController(qa.Context);
            controller.Novo(nomeMalicioso, "Prateleira", "1", "100", "XSS001");

            var salvo = qa.Context.Produtos.AsNoTracking()
                .FirstOrDefault(p => p.CodigoBarras == "XSS001");

            // Aceitar o payload intacto e o que permite a injecao.
            Assert.False(salvo is not null && salvo.Nome == nomeMalicioso,
                "O cadastro aceitou o nome com payload de XSS intacto.");
        }

        // 2) O JS nao deve interpolar o nome direto em innerHTML.
        var js = LocalizarArquivo(Path.Combine("wwwroot", "js", "admin-caixa.js"));
        Assert.True(File.Exists(js), $"admin-caixa.js nao encontrado: {js}");

        var conteudo = File.ReadAllText(js);
        var linhaInjetora = conteudo
            .Split('\n')
            .FirstOrDefault(l => l.Contains("innerHTML") && l.Contains("${nome"));

        Assert.True(linhaInjetora is null,
            "admin-caixa.js interpola o nome do produto direto em innerHTML: " +
            (linhaInjetora?.Trim() ?? ""));
    }

    // ---------------------------------------------------------------------
    // BUG: vazamento de ex.Message ao cliente.
    // ListarVendedores, ObterRelatorioResumido, ObterDadosFechamento e
    // RegistrarMovimentacao devolvem a mensagem interna da excecao, o que
    // revela nome de tabela/coluna, caminho de arquivo e detalhes de
    // infraestrutura. O log deve guardar a excecao; a resposta ao cliente
    // deve ser generica.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ListarVendedores_NaoDeveVazarMensagemDeExcecao()
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        // Derruba a tabela para forcar uma excecao real no acesso ao banco.
        qa.Context.Database.ExecuteSqlRaw("DROP TABLE Vendedores");
        qa.Context.ChangeTracker.Clear();

        var result = await controller.ListarVendedores();

        var json = result.RawJson();
        Assert.DoesNotContain("SqliteException", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no such table", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ObterDadosFechamento_NaoDeveVazarMensagemDeExcecao()
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        qa.Context.Database.ExecuteSqlRaw("DROP TABLE Vendas");
        qa.Context.ChangeTracker.Clear();

        var json = controller.ObterDadosFechamento().RawJson();
        Assert.DoesNotContain("SqliteException", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no such table", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrarMovimentacao_NaoDeveVazarMensagemDeExcecao()
    {
        using var qa = new QaContext();
        var controller = new CaixaController(qa.Context);

        // "abc" nao e decimal -> FormatException capturada e devolvida ao cliente.
        var result = controller.RegistrarMovimentacao("Suprimento", "abc", "teste");

        var json = result.RawJson();
        Assert.DoesNotContain("FormatException", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string LocalizarArquivo(string caminhoRelativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ControleEstoqueLoja.csproj")))
                return Path.Combine(dir.FullName, caminhoRelativo);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do projeto web nao encontrada.");
    }
}
