using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControleEstoqueLoja.Security;

/// <summary>
/// Garante que os PAPELIS existam no banco a cada inicializacao.
///
/// Sao apenas nomes ("Dono", "Operador") - NUNCA usuarios e NUNCA senhas.
/// A senha do primeiro Dono e escolhida pela pessoa na tela de Setup, e nao
/// fica no codigo nem no git (o alerta QA_S05 ja apontou segredos
/// versionados no passado; nao repetimos o erro).
///
/// Sem isso, o primeiro login falha com "Role Dono does not exist".
/// </summary>
public static class SeedPapel
{
    public static async Task GarantirAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var papel in new[] { Roles.Dono, Roles.Operador })
        {
            if (!await roleManager.RoleExistsAsync(papel))
            {
                var resultado = await roleManager.CreateAsync(new IdentityRole(papel));
                if (!resultado.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Falha ao criar o papel '{papel}': " +
                        string.Join("; ", resultado.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}
