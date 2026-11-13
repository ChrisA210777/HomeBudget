using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Budget.Infrastructure.Encryption;

/// <summary>Keeps each entity's <c>NameKey</c> in step with its (encrypted) name as it's saved (BUD-166).</summary>
public sealed class NameKeyInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified) || entry.Metadata.NameKeySource() is not { } source)
            {
                continue;
            }

            var name = entry.Property(source);
            var key = entry.Property(EncryptionModelExtensions.NameKeyColumn);
            if (entry.State == EntityState.Added || name.IsModified || key.CurrentValue is null)
            {
                key.CurrentValue = name.CurrentValue is string value ? FieldEncryption.NameKey(value) : null;
            }
        }
    }
}
