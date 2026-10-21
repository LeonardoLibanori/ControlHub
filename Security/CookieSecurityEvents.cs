using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace ControleEstoqueLoja.Security;

public static class CookieSecurityEvents
{
    public static void ConfigureSecurityStampValidator(SecurityStampValidatorOptions options)
    {
        options.ValidationInterval = TimeSpan.Zero;
    }

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var stampValidator = context.HttpContext.RequestServices
            .GetRequiredService<ISecurityStampValidator>();
        await stampValidator.ValidateAsync(context);

        // Nao reemitimos o token antiforgery aqui. A renovacao acontece
        // naturalmente nas respostas GET de pagina (_Layout.cshtml chama
        // @Antiforgery.GetAndStoreTokens). Reemitir no OnValidatePrincipal
        // giraria o cookie do token a cada requisicao autenticada,
        // invalidando o token embutido em paginas ja carregadas e causando
        // 400 em POSTs legítimos (falso-positivo de CSRF).
    }
}