using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace ControleEstoqueLoja.Security;

public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration)
    {
        var allowedHosts = configuration["AllowedHosts"]?
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        if (allowedHosts.Length == 0 || allowedHosts.Any(host => host == "*"))
        {
            throw new InvalidOperationException(
                "Configure AllowedHosts com os hosts reais antes de iniciar em produção.");
        }

        var hasHttpsPort = int.TryParse(configuration["https_port"], out var httpsPort) &&
                           httpsPort is > 0 and <= 65535;
        var hasHttpsUrl = (configuration["urls"] ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
        var hasKestrelHttpsEndpoint = configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .Any(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
        var hasTrustedProxy = configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren().Any();
        if (!hasHttpsPort && !hasHttpsUrl && !hasKestrelHttpsEndpoint && !hasTrustedProxy)
        {
            throw new InvalidOperationException(
                "Configure um endpoint HTTPS/porta ou os IPs de um proxy TLS confiável antes de iniciar em produção.");
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings__DefaultConnection antes de iniciar em produção.");
        }

        var sqlite = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(sqlite.DataSource) ||
            sqlite.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(sqlite.DataSource))
        {
            throw new InvalidOperationException(
                "Em produção, configure o SQLite com um caminho absoluto em diretório persistente e gravável.");
        }

        var keysDirectory = configuration["DataProtection:KeysDirectory"];
        if (string.IsNullOrWhiteSpace(keysDirectory) || !Path.IsPathFullyQualified(keysDirectory))
        {
            throw new InvalidOperationException(
                "Configure DataProtection__KeysDirectory com um caminho absoluto persistente antes de iniciar em produção.");
        }
    }
}
