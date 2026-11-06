using Budget.Shared.Outgoings;

namespace Budget.Shared.Budgeting;

public static class BudgetRoutes
{
    /// <summary>GET: the budget for a month (this month when <paramref name="year"/> is null).</summary>
    public static string Month(Guid householdId, int? year = null, int? month = null) =>
        year is { } y && month is { } m
            ? $"/api/households/{householdId}/budget?month={y:D4}-{m:D2}"
            : $"/api/households/{householdId}/budget";
}

/// <summary>
/// Income against outgoings for one month (BUD-53, ADR-011). Each figure is rounded once, exactly as
/// the Income and Outgoings pages show it; the "left" figures are worked out from those, so the
/// sums on the page add up. A negative "left" is a shortfall.
/// </summary>
public sealed record BudgetMonthResponse(
    int Year,
    int Month,
    string CurrencyCode,
    decimal Income,
    decimal Essential,
    decimal Preferred,
    decimal Optional,
    decimal LeftAfterEssential,
    decimal LeftAfterPreferred,
    decimal LeftAfterEverything,
    IReadOnlyList<BudgetKindLine> ByKind,
    IReadOnlyList<BudgetCategoryLine> ByCategory)
{
    public decimal Outgoings => Essential + Preferred + Optional;
}

public sealed record BudgetKindLine(OutgoingKindDto Kind, decimal Amount);

/// <summary>A category's monthly total, in the household's category order; only categories with something in them.</summary>
public sealed record BudgetCategoryLine(Guid CategoryId, string Name, decimal Amount);
