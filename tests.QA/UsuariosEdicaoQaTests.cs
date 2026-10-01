using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using ControleEstoqueLoja;
using ControleEstoqueLoja.Controllers;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using ControleEstoqueLoja.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace tests.QA;

public class UsuariosEdicaoQaTests
{
    [Fact]
    public async Task EditarEmail_MantemUsernameSincronizadoEAtualizaSecurityStamp()
    {
        using var fixture = new IdentityFixture();
        var dono = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        var operador = await fixture.CriarUsuario("operador@example.test", Roles.Operador);
        var stampAnterior = operador.SecurityStamp;
        var controller = fixture.CriarController(dono);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = operador.Id,
            Nome = operador.NomeExibicao!,
            Email = "operador.novo@example.test",
            Papel = Roles.Operador
        });

        Assert.IsType<RedirectToActionResult>(result);
        var atualizado = await fixture.UserManager.FindByIdAsync(operador.Id);
        Assert.Equal("operador.novo@example.test", atualizado!.Email);
        Assert.Equal(atualizado.Email, atualizado.UserName);
        Assert.NotEqual(stampAnterior, atualizado.SecurityStamp);
        Assert.Equal(0, fixture.SignInManager.RefreshCount);
    }

    [Fact]
    public async Task EditarEmail_DuplicadoEhRejeitadoSemAlterarConta()
    {
        using var fixture = new IdentityFixture();
        var dono = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        var operador = await fixture.CriarUsuario("operador@example.test", Roles.Operador);
        await fixture.CriarUsuario("outro@example.test", Roles.Operador);
        var controller = fixture.CriarController(dono);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = operador.Id,
            Nome = operador.NomeExibicao!,
            Email = "outro@example.test",
            Papel = Roles.Operador
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState.Values.SelectMany(value => value.Errors),
            error => error.ErrorMessage.Contains("já existe", StringComparison.OrdinalIgnoreCase));
        fixture.Context.ChangeTracker.Clear();
        var unchanged = await fixture.UserManager.FindByIdAsync(operador.Id);
        Assert.Equal("operador@example.test", unchanged!.Email);
        Assert.Equal("operador@example.test", unchanged.UserName);
    }

    [Fact]
    public async Task EditarComSenhasEmBranco_NaoAlteraSenhaNemSecurityStamp()
    {
        using var fixture = new IdentityFixture();
        var dono = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        var operador = await fixture.CriarUsuario("operador@example.test", Roles.Operador);
        var hashAnterior = operador.PasswordHash;
        var stampAnterior = operador.SecurityStamp;
        var controller = fixture.CriarController(dono);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = operador.Id,
            Nome = operador.NomeExibicao!,
            Email = operador.Email!,
            Papel = Roles.Operador,
            NovaSenha = string.Empty,
            ConfirmarNovaSenha = string.Empty
        });

        Assert.IsType<RedirectToActionResult>(result);
        var unchanged = await fixture.UserManager.FindByIdAsync(operador.Id);
        Assert.Equal(hashAnterior, unchanged!.PasswordHash);
        Assert.Equal(stampAnterior, unchanged.SecurityStamp);
        Assert.True(await fixture.UserManager.CheckPasswordAsync(unchanged, "SenhaAntiga123"));
    }

    [Fact]
    public async Task EditarComNovaSenha_ResetaSenhaEAtualizaSecurityStamp()
    {
        using var fixture = new IdentityFixture();
        var dono = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        var operador = await fixture.CriarUsuario("operador@example.test", Roles.Operador);
        var stampAnterior = operador.SecurityStamp;
        var controller = fixture.CriarController(dono);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = operador.Id,
            Nome = operador.NomeExibicao!,
            Email = operador.Email!,
            Papel = Roles.Operador,
            NovaSenha = "SenhaNova456",
            ConfirmarNovaSenha = "SenhaNova456"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var atualizado = await fixture.UserManager.FindByIdAsync(operador.Id);
        Assert.False(await fixture.UserManager.CheckPasswordAsync(atualizado!, "SenhaAntiga123"));
        Assert.True(await fixture.UserManager.CheckPasswordAsync(atualizado!, "SenhaNova456"));
        Assert.NotEqual(stampAnterior, atualizado!.SecurityStamp);
    }

    [Fact]
    public async Task EditarComConfirmacaoDiferente_RejeitaSemAlterarSenha()
    {
        using var fixture = new IdentityFixture();
        var dono = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        var operador = await fixture.CriarUsuario("operador@example.test", Roles.Operador);
        var controller = fixture.CriarController(dono);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = operador.Id,
            Nome = operador.NomeExibicao!,
            Email = operador.Email!,
            Papel = Roles.Operador,
            NovaSenha = "SenhaNova456",
            ConfirmarNovaSenha = "SenhaDivergente789"
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(EditarUsuarioViewModel.ConfirmarNovaSenha)]!.Errors,
            error => error.ErrorMessage == "As senhas não conferem.");
        Assert.True(await fixture.UserManager.CheckPasswordAsync(operador, "SenhaAntiga123"));
    }

    [Fact]
    public async Task RebaixarUnicoDono_EhBloqueadoComMensagemClara()
    {
        using var fixture = new IdentityFixture();
        var unicoDono = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        var controller = fixture.CriarController(unicoDono);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = unicoDono.Id,
            Nome = unicoDono.NomeExibicao!,
            Email = unicoDono.Email!,
            Papel = Roles.Operador
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(EditarUsuarioViewModel.Papel)]!.Errors,
            error => error.ErrorMessage == "Esta é a única conta Dono. Crie outro Dono antes de rebaixar esta.");
        Assert.True(await fixture.UserManager.IsInRoleAsync(unicoDono, Roles.Dono));
        Assert.Equal(0, fixture.SignInManager.RefreshCount);
    }

    [Fact]
    public async Task DonoEditandoPropriaConta_AtualizaStampEReemiteCookie()
    {
        using var fixture = new IdentityFixture();
        var donoAtual = await fixture.CriarUsuario("dono@example.test", Roles.Dono);
        await fixture.CriarUsuario("operador@example.test", Roles.Operador);
        await fixture.CriarUsuario("outro-dono@example.test", Roles.Dono);
        var stampAnterior = donoAtual.SecurityStamp;
        var controller = fixture.CriarController(donoAtual);

        var result = await controller.Editar(new EditarUsuarioViewModel
        {
            Id = donoAtual.Id,
            Nome = donoAtual.NomeExibicao!,
            Email = "dono.novo@example.test",
            Papel = Roles.Operador
        });

        Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Caixa/Index", ((RedirectResult)result).Url);
        Assert.Equal(1, fixture.SignInManager.RefreshCount);
        Assert.Equal(donoAtual.Id, fixture.SignInManager.RefreshedUserId);
        var atualizado = await fixture.UserManager.FindByIdAsync(donoAtual.Id);
        Assert.Equal("dono.novo@example.test", atualizado!.UserName);
        Assert.False(await fixture.UserManager.IsInRoleAsync(atualizado, Roles.Dono));
        Assert.True(await fixture.UserManager.IsInRoleAsync(atualizado, Roles.Operador));
        Assert.NotEqual(stampAnterior, atualizado.SecurityStamp);
    }

    [Fact]
    public async Task SecurityStampEhValidadoAntesDaRenovacaoAntiforgery()
    {
        var stampValidator = new RecordingSecurityStampValidator();
        var antiforgery = new RecordingAntiforgery();
        var context = CriarCookieContext(stampValidator, antiforgery, incluirTokenRecente: true);

        await CookieSecurityEvents.ValidatePrincipalAsync(context);

        Assert.Equal(1, stampValidator.ValidationCount);
        Assert.Equal(0, antiforgery.GetAndStoreTokensCount);
    }

    [Fact]
    public async Task SecurityStampRejeitadoNaoRenovaAntiforgeryNemMantemPrincipal()
    {
        var stampValidator = new RecordingSecurityStampValidator(rejeitar: true);
        var antiforgery = new RecordingAntiforgery();
        var context = CriarCookieContext(stampValidator, antiforgery, incluirTokenRecente: false);

        await CookieSecurityEvents.ValidatePrincipalAsync(context);

        Assert.Equal(1, stampValidator.ValidationCount);
        Assert.Equal(0, antiforgery.GetAndStoreTokensCount);
        Assert.Null(context.Principal);
    }

    [Fact]
    public void ConfiguracaoExigeEmailUnicoStampImediatoEAntiforgeryNoPost()
    {
        var stampOptions = new SecurityStampValidatorOptions();
        CookieSecurityEvents.ConfigureSecurityStampValidator(stampOptions);
        Assert.Equal(TimeSpan.Zero, stampOptions.ValidationInterval);

        var raiz = LocalizarRaizProjeto();
        var program = File.ReadAllText(Path.Combine(raiz, "Program.cs"));
        Assert.Contains("options.User.RequireUniqueEmail = true;", program);

        var post = typeof(UsuariosController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(UsuariosController.Editar) &&
                              method.GetParameters().Single().ParameterType == typeof(EditarUsuarioViewModel));
        Assert.NotNull(post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private static CookieValidatePrincipalContext CriarCookieContext(
        RecordingSecurityStampValidator stampValidator,
        RecordingAntiforgery antiforgery,
        bool incluirTokenRecente)
    {
        var services = new ServiceCollection()
            .AddSingleton<ISecurityStampValidator>(stampValidator)
            .AddSingleton<IAntiforgery>(antiforgery)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            IdentityConstants.ApplicationScheme,
            typeof(CookieAuthenticationHandler));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "usuario-teste") },
            IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow };
        if (incluirTokenRecente)
        {
            properties.Items["AntiforgeryRenewedAt"] = DateTimeOffset.UtcNow
                .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        var ticket = new AuthenticationTicket(principal, properties, scheme.Name);
        return new CookieValidatePrincipalContext(
            httpContext, scheme, new CookieAuthenticationOptions(), ticket);
    }

    private static string LocalizarRaizProjeto()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ControleEstoqueLoja.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do projeto web nao encontrada.");
    }

    private sealed class IdentityFixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public IdentityFixture()
        {
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddControllersWithViews();
            services.AddHttpContextAccessor();
            services.AddDataProtection();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
            services.AddIdentity<Usuario, IdentityRole>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequireDigit = true;
                    options.Password.RequiredLength = 6;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireNonAlphanumeric = false;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Context.Database.EnsureCreated();
            UserManager = _scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            SignInManager = CriarSignInManager();
        }

        public ApplicationDbContext Context { get; }
        public UserManager<Usuario> UserManager { get; }
        public RecordingSignInManager SignInManager { get; }

        public async Task<Usuario> CriarUsuario(string email, string papel)
        {
            var roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (await roleManager.FindByNameAsync(papel) is null)
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(papel));
                Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }

            var usuario = new Usuario
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                NomeExibicao = email
            };
            var created = await UserManager.CreateAsync(usuario, "SenhaAntiga123");
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
            var assigned = await UserManager.AddToRoleAsync(usuario, papel);
            Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(error => error.Description)));
            return usuario;
        }

        public UsuariosController CriarController(Usuario usuarioAtual)
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = _scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, usuarioAtual.Id),
                        new Claim(ClaimTypes.Name, usuarioAtual.UserName!)
                    },
                    IdentityConstants.ApplicationScheme))
            };
            _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

            var controller = new UsuariosController(
                UserManager, SignInManager, Context,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UsuariosController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext,
                    RouteData = new RouteData(),
                    ActionDescriptor = new ControllerActionDescriptor()
                },
                TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider())
            };
            return controller;
        }

        private RecordingSignInManager CriarSignInManager()
        {
            var services = _scope.ServiceProvider;
            return new RecordingSignInManager(
                UserManager,
                services.GetRequiredService<IHttpContextAccessor>(),
                services.GetRequiredService<IUserClaimsPrincipalFactory<Usuario>>(),
                services.GetRequiredService<IOptions<IdentityOptions>>(),
                services.GetRequiredService<ILogger<SignInManager<Usuario>>>(),
                services.GetRequiredService<IAuthenticationSchemeProvider>(),
                services.GetRequiredService<IUserConfirmation<Usuario>>());
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class RecordingSignInManager : SignInManager<Usuario>
    {
        public RecordingSignInManager(
            UserManager<Usuario> userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<Usuario> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            ILogger<SignInManager<Usuario>> logger,
            IAuthenticationSchemeProvider schemes,
            IUserConfirmation<Usuario> confirmation)
            : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
        {
        }

        public int RefreshCount { get; private set; }
        public string? RefreshedUserId { get; private set; }

        public override Task RefreshSignInAsync(Usuario user)
        {
            RefreshCount++;
            RefreshedUserId = user.Id;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class RecordingSecurityStampValidator(bool rejeitar = false) : ISecurityStampValidator
    {
        public int ValidationCount { get; private set; }

        public Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            ValidationCount++;
            if (rejeitar)
                context.RejectPrincipal();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAntiforgery : IAntiforgery
    {
        public int GetAndStoreTokensCount { get; private set; }

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
        {
            GetAndStoreTokensCount++;
            return new AntiforgeryTokenSet("request", "cookie", "__RequestVerificationToken", "X-CSRF-TOKEN");
        }

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
            new("request", "cookie", "__RequestVerificationToken", "X-CSRF-TOKEN");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }
}