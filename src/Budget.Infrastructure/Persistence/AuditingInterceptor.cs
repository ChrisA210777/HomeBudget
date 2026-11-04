using Budget.Domain.Common;
using Budget.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Budget.Infrastructure.Persistence;

/// <summary>
/// Fills in the audit fields of <see cref="IAuditable"/> entities whenever changes are saved:
/// created and modified details on insert, modified details on update. Creation details can't
/// be changed after insert.
/// </summary>
public sealed class AuditingInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        ApplyAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        ApplyAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditFields(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = userId;
                    entry.Property(nameof(IAuditable.ModifiedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.ModifiedBy)).CurrentValue = userId;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    entry.Property(nameof(IAuditable.ModifiedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.ModifiedBy)).CurrentValue = userId;
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                default:
                    break;
            }
        }
    }
}
