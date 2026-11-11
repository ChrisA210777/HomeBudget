using Budget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Budget.Server.Hosting;

/// <summary>
/// Readiness (BUD-12): the database can be reached. Only on <see cref="Budget.Shared.ApiRoutes.Ready"/>, so the plain
/// health check doesn't keep waking the free-tier database.
/// </summary>
public sealed class DatabaseHealthCheck(BudgetDbContext db) : IHealthCheck
{
    public const string ReadyTag = "ready";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Can't reach the database.");

    public static HealthCheckOptions Liveness { get; } = new() { Predicate = check => !check.Tags.Contains(ReadyTag) };

    public static HealthCheckOptions Readiness { get; } = new() { Predicate = check => check.Tags.Contains(ReadyTag) };
}
