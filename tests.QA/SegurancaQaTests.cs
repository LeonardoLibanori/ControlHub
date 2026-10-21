using System.Reflection;
using System.Security.Claims;
using System.Threading;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Models;
using ControleEstoqueLoja.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            .Where(t => t.GetCustomAttributes(inherit: false)
                .All(a => a.GetType().FullName != "Microsoft.AspNetCore.Authorization.AuthorizeAttribute"))
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
            // Sem HttpContext o TempData não pode ser escrito; ComTempData() é apenas
            // infraestrutura de teste. NÃO altera o que este teste verifica: o nome
            // com payload de XSS continua sendo gravado (o teste segue vermelho por isso).
            var controller = new HomeController(qa.Context).ComTempData();
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
        var result = controller.RegistrarSuprimento("abc", "teste");

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

    // ---------------------------------------------------------------------
    // REGRESSAO (CSRF): validacao do cookie de autenticacao (OnValidatePrincipal)
    // NAO deve girar o token antiforgery. Se girar, o cookie do token e
    // sobrescrito a cada requisicao autenticada enquanto o request-token
    // embutido na pagina ja carregada continua apontando pro cookie anterior,
    // e POSTs legítimos recebem 400 (falso-positivo de CSRF).
    // ---------------------------------------------------------------------
    [Fact]
    public void ValidacaoDeCookie_NaoGiraTokenAntiforgery()
    {
        // Arrange: IAntiforgery falso que conta chamadas a GetAndStoreTokens.
        var chamadasGetAndStoreTokens = 0;
        var antiforgery = new AntiforgeryFake(() => Interlocked.Increment(ref chamadasGetAndStoreTokens));

        var services = new ServiceCollection()
            .AddSingleton<IAntiforgery>(antiforgery)
            // ISecurityStampValidator eh necessario pelo ValidatePrincipalAsync.
            .AddSingleton<ISecurityStampValidator>(new NoOpSecurityStampValidator())
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "usuario-1") }, "Cookie"));

        var ticket = new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme);
        var contexto = new CookieValidatePrincipalContext(
            httpContext,
            new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            ticket)
        {
            Principal = principal
        };

        // Act: simula a validacao do cookie de autenticacao numa requisicao autenticada.
        CookieSecurityEvents.ValidatePrincipalAsync(contexto).GetAwaiter().GetResult();

        // Assert: o token antiforgery NAO deve ter sido reemitido.
        Assert.Equal(0, chamadasGetAndStoreTokens);
    }

    // Implementacao no-op de ISecurityStampValidator para testes unitarios.
    private sealed class NoOpSecurityStampValidator : ISecurityStampValidator
    {
        public Task ValidateAsync(CookieValidatePrincipalContext context) => Task.CompletedTask;
    }

    [Fact]
    public void TokenAntiforgery_ParCookieRequest_Valida()
    {
        // Arrange: servico real de antiforgery (mesma configuracao de Program.cs).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        services.AddDataProtection();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var antiforgery = serviceProvider.GetRequiredService<IAntiforgery>();

        // Simula o GET da pagina: emite o cookie do token e devolve o request token.
        var tokens = antiforgery.GetAndStoreTokens(httpContext);

        // Act: obtem os tokens novamente (GetTokens) e confere que sao validos (nao nulos).
        var tokens2 = antiforgery.GetTokens(httpContext);

        // Assert: tokens validos (nao lancou excecao, nao sao nulos).
        Assert.NotNull(tokens);
        Assert.NotNull(tokens2);
        Assert.Equal(tokens.RequestToken, tokens2.RequestToken);
    }

    // Falsificacao minima de IAntiforgery para rastrear chamadas a GetAndStoreTokens.
    private sealed class AntiforgeryFake : IAntiforgery
    {
        private readonly Action _onGetAndStoreTokens;

        public AntiforgeryFake(Action onGetAndStoreTokens) => _onGetAndStoreTokens = onGetAndStoreTokens;

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context)
        {
            _onGetAndStoreTokens();
            return new AntiforgeryTokenSet("cookie-token", "request-token", "__RequestVerificationToken", "X-CSRF-TOKEN");
        }

        public AntiforgeryTokenSet GetTokens(HttpContext context) =>
            new AntiforgeryTokenSet("cookie-token", "request-token", "__RequestVerificationToken", "X-CSRF-TOKEN");

        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);

        public void SetCookieTokenAndHeader(HttpContext context) { }

        public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;

        public Task ValidateTokens(HttpContext context, string cookieToken, string requestToken) =>
            Task.CompletedTask;
    }
}
