using Budget.Infrastructure.Encryption;
using Budget.Infrastructure.Persistence;

namespace Budget.Server.Hosting;

/// <summary>
/// When the app starts, encrypts anything stored before names and free text were encrypted (BUD-166), and fills in
/// missing name keys. After the first run there's nothing left to do, and it finishes straight away.
/// </summary>
public sealed partial class EncryptExistingData(IServiceScopeFactory scopes, ILogger<EncryptExistingData> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var changed = await ExistingDataEncryptor.RunAsync(db, stoppingToken);
            if (changed > 0)
            {
                LogEncrypted(changed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database may not be ready (e.g. asleep or being migrated); it's tried again next time the app starts.
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Encrypted {Count} rows stored before encryption")]
    private partial void LogEncrypted(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't encrypt data stored before encryption")]
    private partial void LogFailed(Exception exception);
}
