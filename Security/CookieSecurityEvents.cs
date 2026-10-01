using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace ControleEstoqueLoja.Security;

public static class CookieSecurityEvents
{
    private const string AntiforgeryRenewedAtKey = "AntiforgeryRenewedAt";
    private static readonly TimeSpan AntiforgeryRenewalInterval = TimeSpan.FromMinutes(30);

    public static void ConfigureSecurityStampValidator(SecurityStampValidatorOptions options)
    {
        options.ValidationInterval = TimeSpan.Zero;
    }

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var stampValidator = context.HttpContext.RequestServices
            .GetRequiredService<ISecurityStampValidator>();
        await stampValidator.ValidateAsync(context);

        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        var now = DateTimeOffset.UtcNow;
        if (context.Properties.Items.TryGetValue(AntiforgeryRenewedAtKey, out var lastRenewed) &&
            long.TryParse(lastRenewed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) &&
            now - DateTimeOffset.FromUnixTimeSeconds(timestamp) < AntiforgeryRenewalInterval)
        {
            return;
        }

        var antiforgery = context.HttpContext.RequestServices
            .GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context.HttpContext);
        context.Properties.UpdateTokenValue(tokens.FormFieldName, tokens.RequestToken);
        context.Properties.Items[AntiforgeryRenewedAtKey] = now.ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }
}