using Budget.Domain.Activity;
using Budget.Domain.Households;
using Budget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Activity;

/// <summary>
/// Removes recent activity older than three months (BUD-139) and data download log entries older than twelve months
/// (BUD-141), when the app starts and then once a day.
/// </summary>
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

                var downloadsCutoff = time.GetUtcNow() - DataDownload.KeptFor;
                var downloads = await db.DataDownloads.IgnoreQueryFilters().Where(d => d.At < downloadsCutoff).ExecuteDeleteAsync(stoppingToken);
                if (downloads > 0)
                {
                    LogDownloadsRemoved(downloads, downloadsCutoff);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} data download log entries older than {Cutoff}")]
    private partial void LogDownloadsRemoved(int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't remove old recent activity or download log entries")]
    private partial void LogFailed(Exception exception);
}
