using System.Globalization;
using Budget.Domain.Common;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Budgeting;
using Budget.Shared.Outgoings;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Budgeting;

/// <summary>The monthly budget: income less outgoings by priority (BUD-53). All roles can view.</summary>
public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(HouseholdRoutes.Base + "/budget", GetMonthAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        return app;
    }

    private static async Task<IResult> GetMonthAsync(Guid householdId, string? month, BudgetDbContext db, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        int year, monthNumber;
        if (string.IsNullOrEmpty(month))
        {
            (year, monthNumber) = (today.Year, today.Month);
        }
        else if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            (year, monthNumber) = (parsed.Year, parsed.Month);
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["month"] = ["Use the format yyyy-MM, e.g. 2026-11."] });
        }

        var streams = await db.IncomeStreams.Include(s => s.AmountChanges).Include(s => s.StatusPeriods).AsSplitQuery().AsNoTracking().ToListAsync();
        var outgoings = await db.Outgoings.Include(o => o.AmountChanges).Include(o => o.StatusPeriods).AsSplitQuery().AsNoTracking().ToListAsync();
        var categories = await db.OutgoingCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync();
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();

        // The same rules as the Income and Outgoings pages: unrounded amounts, each total rounded once.
        var income = Values.RoundForDisplay(streams.Sum(s => s.AmountForMonth(year, monthNumber)));
        var amounts = outgoings
            .Select(o => (Outgoing: o, Amount: o.MonthlyEquivalentForMonth(year, monthNumber)))
            .Where(x => x.Amount != 0m)
            .ToList();
        decimal Total(Func<Outgoing, bool> include) => Values.RoundForDisplay(amounts.Where(x => include(x.Outgoing)).Sum(x => x.Amount));

        var essential = Total(o => o.Priority == OutgoingPriority.Essential);
        var preferred = Total(o => o.Priority == OutgoingPriority.Preferred);
        var optional = Total(o => o.Priority == OutgoingPriority.Optional);

        var byKind = Enum.GetValues<OutgoingKind>()
            .Select(kind => new BudgetKindLine((OutgoingKindDto)kind, Total(o => o.Kind == kind)))
            .ToList();
        var byCategory = categories
            .Select(c => new BudgetCategoryLine(c.Id, c.Name, Total(o => o.CategoryId == c.Id)))
            .Where(line => amounts.Any(x => x.Outgoing.CategoryId == line.CategoryId))
            .ToList();

        return Results.Ok(new BudgetMonthResponse(
            year,
            monthNumber,
            currency,
            income,
            essential,
            preferred,
            optional,
            income - essential,
            income - essential - preferred,
            income - essential - preferred - optional,
            byKind,
            byCategory));
    }
}
