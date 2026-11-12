using Budget.Domain.Activity;
using Budget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Activity;

/// <summary>Removes recent activity older than three months (BUD-139), when the app starts and then once a day.</summary>
public sealed partial class ActivityCleanup(IServiceScopeFactory scopes, TimeProvider time, ILogger<ActivityCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
                var cutoff = time.GetUtcNow() - ActivityEntry.KeptFor;
                var removed = await db.ActivityEntries.IgnoreQueryFilters().Where(a => a.At < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (removed > 0)
                {
                    LogRemoved(removed, cutoff);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The database may not be ready yet, e.g. while it's being migrated; it's tried again tomorrow.
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} recent activity entries older than {Cutoff}")]
    private partial void LogRemoved(int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't remove old recent activity")]
    private partial void LogFailed(Exception exception);
}
