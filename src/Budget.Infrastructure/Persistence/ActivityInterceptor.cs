using Budget.Domain.Activity;
using Budget.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Budget.Infrastructure.Persistence;

/// <summary>
/// Completes recent activity (BUD-139) as it's saved, in the same transaction as the change it describes: who made
/// it (their name in the household, as it is now) and when. An edit the same person made to the same item within ten
/// minutes of their last one updates that entry instead of adding another.
/// </summary>
public sealed class ActivityInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        CompleteAsync(eventData.Context, sync: true, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await CompleteAsync(eventData.Context, sync: false, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task CompleteAsync(DbContext? context, bool sync, CancellationToken cancellationToken)
    {
        if (context is not BudgetDbContext db)
        {
            return;
        }

        var added = db.ChangeTracker.Entries<ActivityEntry>().Where(e => e.State == EntityState.Added).ToList();
        if (added.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var userId = currentUser.UserId;
        var names = new Dictionary<Guid, string>();
        foreach (var entry in added)
        {
            var activity = entry.Entity;
            if (!names.TryGetValue(activity.HouseholdId, out var name))
            {
                name = await ActorNameAsync(db, activity.HouseholdId, userId, sync, cancellationToken);
                names[activity.HouseholdId] = name;
            }

            activity.Stamp(now, userId, name);
            if (activity.MergeKey is null)
            {
                continue;
            }

            var since = now - ActivityEntry.MergeWithin;
            var query = db.ActivityEntries.IgnoreQueryFilters()
                .Where(a => a.HouseholdId == activity.HouseholdId && a.MergeKey == activity.MergeKey && a.UserId == userId && a.At >= since)
                .OrderByDescending(a => a.At);
            var latest = sync ? query.FirstOrDefault() : await query.FirstOrDefaultAsync(cancellationToken);
            if (latest is not null && latest.Id != activity.Id && latest.CanMerge(activity))
            {
                latest.Merge(activity);
                entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>Their name as a person in the household, e.g. "Chris"; otherwise their login's name.</summary>
    private static async Task<string> ActorNameAsync(BudgetDbContext db, Guid householdId, Guid? userId, bool sync, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return "The app";
        }

        var person = db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId && p.LinkedUserId == id).Select(p => p.DisplayName);
        var user = db.Users.Where(u => u.Id == id).Select(u => u.DisplayName);
        var name = sync ? person.FirstOrDefault() ?? user.FirstOrDefault()
            : await person.FirstOrDefaultAsync(cancellationToken) ?? await user.FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(name) ? "Someone" : name;
    }
}
