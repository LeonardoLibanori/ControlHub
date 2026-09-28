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
// BUG-19: EnsureCreated() criava o schema a partir do MODELO ATUAL e ignorava
// as migrations. Numa base já existente, EnsureCreated() não altera nada: as
// colunas/índices/FKs criados por migrations posteriores nunca eram aplicados e a
// aplicação quebrava em runtime. Migrate() aplica as migrations pendentes e cria
// a base do zero apenas quando ela ainda não existe.
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