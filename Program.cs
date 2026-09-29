using System.IO;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;


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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();