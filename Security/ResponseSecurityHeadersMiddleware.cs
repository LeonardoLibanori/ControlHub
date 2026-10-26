using Microsoft.AspNetCore.Http;

namespace ControleEstoqueLoja.Security;

/// <summary>
/// Adds response headers without enforcing a Content Security Policy yet. The
/// report-only policy deliberately omits inline script/style allowances so
/// browsers can report the legacy inline code that must be migrated before
/// enforcement is safe.
/// </summary>
public sealed class ResponseSecurityHeadersMiddleware
{
    public const string ContentSecurityPolicyReportOnly =
        "default-src 'self'; " +
        "base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'; " +
        "script-src 'self' https://cdn.jsdelivr.net; " +
        "style-src 'self' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net data:; " +
        "img-src 'self' data: blob:; connect-src 'self'";

    private readonly RequestDelegate _next;

    public ResponseSecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicyReportOnly;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Frame-Options"] = "DENY";

        return _next(context);
    }
}
