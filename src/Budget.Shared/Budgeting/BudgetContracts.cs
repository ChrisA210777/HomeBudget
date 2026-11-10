using Budget.Shared.Income;
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
    IReadOnlyList<BudgetCategoryLine> ByCategory,
    BudgetComparison? Comparison = null)
{
    public decimal Outgoings => Essential + Preferred + Optional;
}

public sealed record BudgetKindLine(OutgoingKindDto Kind, decimal Amount);

/// <summary>A category's monthly total, in the household's category order; only categories with something in them.</summary>
public sealed record BudgetCategoryLine(Guid CategoryId, string Name, decimal Amount, string Icon = CategoryIcons.Default);

/// <summary>
/// How the month compares with the one before (BUD-83, ADR-013): what was left then, the difference,
/// and only the outgoings and income that changed, biggest effect first.
/// </summary>
public sealed record BudgetComparison(int Year, int Month, decimal LeftAfterEverything, decimal Difference, IReadOnlyList<BudgetChangeLine> Changes);

/// <summary>Why an amount differs from the month before.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BudgetChangeReasonDto>))]
public enum BudgetChangeReasonDto
{
    New,
    Ended,
    Paused,
    Resumed,
    AmountChanged,
    Reduced,
    BackToUsual,
}

/// <summary>
/// An outgoing or income whose budget amount changed. <see cref="Effect"/> is the change in money left:
/// negative when it leaves less (a price rise, less income). <see cref="Comment"/> is the comment on the
/// change, e.g. "Annual price rise"; <see cref="Status"/> is the income status behind a reduction.
/// </summary>
public sealed record BudgetChangeLine(
    bool IsIncome,
    Guid Id,
    string Name,
    decimal Previous,
    decimal Current,
    decimal Effect,
    BudgetChangeReasonDto Reason,
    string? Comment,
    IncomeStatusReasonDto? Status = null);
