using System.Globalization;
using Budget.Domain.Budgeting;
using Budget.Domain.Common;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Budgeting;
using Budget.Shared.Households;
using Budget.Shared.Income;
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
        var categories = await db.OutgoingCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Icon }).ToListAsync();
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();

        var budget = TotalsFor(streams, outgoings, year, monthNumber);

        var amounts = outgoings
            .Select(o => (Outgoing: o, Amount: o.MonthlyEquivalentForMonth(year, monthNumber)))
            .Where(x => x.Amount != 0m)
            .ToList();
        decimal Total(Func<Outgoing, bool> include) => Values.RoundForDisplay(amounts.Where(x => include(x.Outgoing)).Sum(x => x.Amount));

        var byKind = Enum.GetValues<OutgoingKind>()
            .Select(kind => new BudgetKindLine((OutgoingKindDto)kind, Total(o => o.Kind == kind)))
            .ToList();
        var byCategory = categories
            .Select(c => new BudgetCategoryLine(c.Id, c.Name, Total(o => o.CategoryId == c.Id), c.Icon))
            .Where(line => amounts.Any(x => x.Outgoing.CategoryId == line.CategoryId))
            .ToList();

        // What changed since the month before, and why (BUD-83, ADR-013).
        var previousMonth = new DateOnly(year, monthNumber, 1).AddMonths(-1);
        var previous = TotalsFor(streams, outgoings, previousMonth.Year, previousMonth.Month);
        var changes = MonthComparison.Changes(outgoings, streams, year, monthNumber)
            .Select(c => new BudgetChangeLine(
                c.IsIncome, c.Id, c.Name, c.Previous, c.Current, c.Effect, (BudgetChangeReasonDto)c.Reason, c.Comment, (IncomeStatusReasonDto?)c.Status))
            .ToList();
        var comparison = new BudgetComparison(
            previousMonth.Year, previousMonth.Month, previous.Left, budget.Left - previous.Left, changes);

        return Results.Ok(new BudgetMonthResponse(
            year,
            monthNumber,
            currency,
            budget.Income,
            budget.Essential,
            budget.Preferred,
            budget.Optional,
            budget.Income - budget.Essential,
            budget.Income - budget.Essential - budget.Preferred,
            budget.Left,
            byKind,
            byCategory,
            comparison));
    }

    private readonly record struct Totals(decimal Income, decimal Essential, decimal Preferred, decimal Optional)
    {
        public decimal Left => Income - Essential - Preferred - Optional;
    }

    /// <summary>
    /// The same rules as the Income and Outgoings pages: unrounded amounts, each total rounded once.
    /// </summary>
    private static Totals TotalsFor(List<IncomeStream> streams, List<Outgoing> outgoings, int year, int month)
    {
        decimal Total(OutgoingPriority priority) =>
            Values.RoundForDisplay(outgoings.Where(o => o.Priority == priority).Sum(o => o.MonthlyEquivalentForMonth(year, month)));

        return new Totals(
            Values.RoundForDisplay(streams.Sum(s => s.AmountForMonth(year, month))),
            Total(OutgoingPriority.Essential),
            Total(OutgoingPriority.Preferred),
            Total(OutgoingPriority.Optional));
    }
}
