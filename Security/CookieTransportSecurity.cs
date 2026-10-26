using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ControleEstoqueLoja.Security;

public static class CookieTransportSecurity
{
    public static CookieSecurePolicy SecurePolicy(bool requireSecureTransport) =>
        requireSecureTransport ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

    public static void Configure(CookieAuthenticationOptions options, bool requireSecureTransport)
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = SecurePolicy(requireSecureTransport);
    }

    public static void Configure(AntiforgeryOptions options, bool requireSecureTransport)
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = SecurePolicy(requireSecureTransport);
    }
}
