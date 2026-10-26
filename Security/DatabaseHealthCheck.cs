using ControleEstoqueLoja.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ControleEstoqueLoja.Security;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ApplicationDbContext db, ILogger<DatabaseHealthCheck> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Database health check failed ({ExceptionType}).", ex.GetType().Name);
            return HealthCheckResult.Unhealthy();
        }
    }
}
