using System.IO;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using ControleEstoqueLoja.Security;


var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Substitua a linha do AddDbContext por esta:
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Isso vai imprimir no console do Visual Studio exatamente onde o arquivo est�
Console.WriteLine($"O BANCO ATIVO �: {connectionString}");
// ----------------------------------------------

// Adicione isso ANTES do builder.Build()
// ------------------------------------------------------------------
// BUG-05 (seguranca): os POSTs de CaixaController/HomeController exigem
// token anti-forgery ([ValidateAntiForgeryToken]). As views ja emitem
// @Html.AntiForgeryToken() no <form>, mas os POSTs feitos por fetch() no
// admin-caixa.js (FinalizarVenda, RegistrarMovimentacao, zerar dia,
// cadastrar/excluir vendedor) nao passam por formulario: o token precisa
// vir no header X-CSRF-TOKEN, lido de um <meta> renderizado no layout.
// ------------------------------------------------------------------
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();

// ------------------------------------------------------------------
// BUG-13: autenticacao e autorizacao (Fase de seguranca, 29/09/2026).
//
// Identity sobre o mesmo ApplicationDbContext (SQLite): as tabelas do login
// (AspNetUsers/Roles/etc) convivem com as de negocio, sem colisao.
//
// LOCKOUT PADRAO HABILITADO (built-in, sem codigo extra): 5 tentativas erradas
// bloqueiam a conta por 5 minutos. Em um PDV com senha compartilhada isso
// trava o balcao contra adivinhacao - exatamente o que queremos.
// ------------------------------------------------------------------
builder.Services.AddIdentity<Usuario, IdentityRole>(options =>
    {
        // Senha minima de loja: 6 caracteres, 1 digito, 1 maiuscula, 1 minuscula.
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Bloqueio progressivo apos tentativas erradas.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ------------------------------------------------------------------
// COOKIE DE AUTENTICACAO
//
// 12 horas com SlidingExpiration: o token RENOVA a cada requisicao, entao
// quem fica logged o turno inteiro no balcao nunca cai. Quem fecha o
// navegador perde depois de 12h. Cobre o dia de loja com folga.
//
// AcoesEventos: a cada ~30 min de atividade reemitem-se o token antiforgery
// junto com o cookie. Sem isso, a tela do PDV aberta o dia todo terminaria
// com POSTReturning400 porque o token antiforgery (cookie) do dia anterior
// ja nao valeria - exatamente o problema relatado no relatorio de QA.
// ------------------------------------------------------------------
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AcessoNegado";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    var reemitir = TimeSpan.FromMinutes(30);
    options.Events.OnValidatePrincipal = async context =>
    {
        // A cada 30 min, renova o token antiforgery do MESMO cookie de sessao.
        if (DateTimeOffset.UtcNow - context.Properties.IssuedUtc < reemitir)
            return;

        var antiforgery = context.HttpContext.RequestServices
            .GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context.HttpContext);
        context.Properties.UpdateTokenValue(tokens.FormFieldName, tokens.RequestToken);
    };

    // ------------------------------------------------------------------
    // ACESSO NEGADO: DUAS RESPOSTAS PARA DOIS CLIENTES DIFERENTES.
    //
    // AccessDeniedPath resolve o operador que CLICA em algo que nao pode ver:
    // ele vai para a tela /Account/AcessoNegado, que explica o motivo.
    //
    // Mas o AccessDeniedPath vale para QUALQUER requisicao - inclusive as
    // feitas por fetch() no admin-caixa.js. Nesse caso o fetch seguia o 302,
    // recebia a pagina HTML de acesso negado com status 200 e o
    //   if (resp.status === 403) { 'Apenas o Dono pode registrar sangria' }
    // nunca acontecia: o operador via "erro inesperado" em vez do motivo real,
    // e um 200 no final dava a impressao de que a operacao tinha passado.
    //
    // Por isso quem NAO e navegacao de navegador (fetch do painel, curl,
    // qualquer cliente de dados) recebe 403 direto, com corpo vazio; quem
    // clicou num link continua indo para a pagina amigavel.
    // O criterio e duplo de proposito: a navegacao pede HTML (Accept:
    // text/html) e NAO manda X-Requested-With. Assim, uma chamada de dados
    // que esqueca o header ainda cai no 403 (seguro por padrao), e nao no
    // comportamento antigo de devolver 200 com a pagina de acesso negado.
    // O redirect de LOGIN nao foi mexido: anonimo segue levando 302 para
    // /Account/Login, que e o comportamento esperado.
    // ------------------------------------------------------------------
    options.Events.OnRedirectToAccessDenied = context =>
    {
        var pedeHtml = context.Request.Headers.Accept
            .ToString()
            .Contains("text/html", StringComparison.OrdinalIgnoreCase);
        var ehAjax = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (ehAjax || !pedeHtml)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// ------------------------------------------------------------------
// POLITICAS NOMEADAS (fonte unica de verdade: Security/Policies.cs).
// Cada rota pergunta "qual policy?" e nunca "qual role?" no codigo.
// Adicionar uma policy nova (ex.: a da camada de IA) e acrescentar UMA linha
// aqui - nada mais precisa mudar.
// ------------------------------------------------------------------
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.PodeOperarPDV, p =>
        p.RequireRole(Roles.Operador, Roles.Dono))
    .AddPolicy(Policies.PodeReimprimirCupom, p =>
        p.RequireRole(Roles.Operador, Roles.Dono))
    .AddPolicy(Policies.PodeRegistrarSuprimento, p =>
        p.RequireRole(Roles.Operador, Roles.Dono))
    .AddPolicy(Policies.PodeRegistrarSangria, p =>
        p.RequireRole(Roles.Dono))
    .AddPolicy(Policies.PodeVerFinanceiro, p =>
        p.RequireRole(Roles.Dono))
    .AddPolicy(Policies.PodeFecharCaixa, p =>
        p.RequireRole(Roles.Dono))
    .AddPolicy(Policies.PodeGerenciarEstoque, p =>
        p.RequireRole(Roles.Dono))
    .AddPolicy(Policies.PodeGerenciarVendedores, p =>
        p.RequireRole(Roles.Dono))
    .AddPolicy(Policies.PodeAcessarAdmin, p =>
        p.RequireRole(Roles.Dono))
    // Camada de IA: declarada e registrada agora, usada quando existir.
    .AddPolicy(Policies.PodeUsarIA, p =>
        p.RequireRole(Roles.Dono));


var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// BUG-13: UseAuthentication ANTES de UseAuthorization - sem o cookie populado,
// o UserPrincipal do cookie nao existe e todo [Authorize] barra, inclusive o
// proprio login.
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

// --- GARANTE QUE O BANCO E AS TABELAS EXISTAM ---
// BUG-19: a criação direta do schema a partir do MODELO ATUAL ignorava as
// migrations. Numa base já existente ela não altera nada: colunas, índices e FKs
// criados por migrations posteriores nunca eram aplicados e a aplicação quebrava
// em runtime. Migrate() aplica as migrations pendentes e cria a base do zero
// apenas quando ela ainda não existe.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Erro ao iniciar o banco: " + ex.Message);
    }
}

// Cria os PAPEIS (Dono/Operador) se ainda nao existirem. Sem usuario e sem
// senha aqui: o primeiro Dono nasce na tela /Account/Setup.
SeedPapel.GarantirAsync(app.Services).GetAwaiter().GetResult();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();