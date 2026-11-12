using Budget.Domain.Activity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Activity;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Activity;

/// <summary>
/// Recent activity (BUD-139): what's been changed in the household, by whom. Everyone in the household can read it;
/// entries are written by each change (see <see cref="ActivityMessages"/>) and can't be changed.
/// </summary>
public static class ActivityEndpoints
{
    private const int MaxTake = 100;

    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup(HouseholdRoutes.Base + "/activity")
            .MapGet("/", ListAsync)
            .RequireAuthorization(HouseholdPolicies.Viewer);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid householdId, int? take, int? days, DateTimeOffset? before, BudgetDbContext db, TimeProvider time)
    {
        var count = Math.Clamp(take ?? 20, 1, MaxTake);
        var kept = (int)ActivityEntry.KeptFor.TotalDays;
        var since = time.GetUtcNow().AddDays(-Math.Clamp(days ?? kept, 1, kept));

        var query = db.ActivityEntries.AsNoTracking().Where(a => a.At >= since);
        if (before is { } cursor)
        {
            query = query.Where(a => a.At < cursor);
        }

        var entries = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(count).ToListAsync();

        // Link to the item only while it still exists.
        var ids = entries.Select(e => e.SubjectId).OfType<Guid>().Distinct().ToList();
        var existing = new HashSet<Guid>();
        existing.UnionWith(await db.IncomeStreams.Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync());
        existing.UnionWith(await db.Outgoings.Where(o => ids.Contains(o.Id)).Select(o => o.Id).ToListAsync());
        existing.UnionWith(await db.Transfers.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync());
        existing.UnionWith(await db.Accounts.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync());
        existing.UnionWith(await db.OutgoingCategories.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync());
        existing.UnionWith(await db.People.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync());

        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();
        var items = entries
            .Select(e => new ActivityItem(
                e.Id, e.At, e.ActorName, e.Summary, (ActivityAreaDto)e.Area, e.SubjectId, e.SubjectId is { } id && existing.Contains(id)))
            .ToList();
        return Results.Ok(new ActivityResponse(currency, items));
    }
}
