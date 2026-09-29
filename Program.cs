using System.IO;
using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja.Data;


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


var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
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