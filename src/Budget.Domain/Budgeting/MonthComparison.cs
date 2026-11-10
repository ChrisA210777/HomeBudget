using Budget.Domain.Common;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Budgeting;

/// <summary>Why an outgoing or income's budget amount differs from the month before (BUD-83).</summary>
public enum BudgetChangeReason
{
    /// <summary>Started this month.</summary>
    New,

    /// <summary>Stopped (outgoing) or ended (income).</summary>
    Ended,

    /// <summary>An outgoing's payments are paused.</summary>
    Paused,

    /// <summary>An outgoing's payments start again after a pause.</summary>
    Resumed,

    /// <summary>An amount change: a price change, pay rise, or a change of schedule.</summary>
    AmountChanged,

    /// <summary>Income is reduced or stopped for a while, e.g. sick leave.</summary>
    Reduced,

    /// <summary>Income is back to its usual amount after being reduced.</summary>
    BackToUsual,
}

/// <summary>
/// One outgoing or income whose budget amount changed from the previous month.
/// <see cref="Effect"/> is the change in money left: negative when it leaves less, e.g. a price rise.
/// <see cref="Comment"/> is the comment on the entry behind the change, e.g. "Annual price rise".
/// <see cref="Status"/> is the income status behind a <see cref="BudgetChangeReason.Reduced"/> or
/// <see cref="BudgetChangeReason.BackToUsual"/> change.
/// </summary>
public sealed record BudgetChange(
    bool IsIncome,
    Guid Id,
    string Name,
    decimal Previous,
    decimal Current,
    decimal Effect,
    BudgetChangeReason Reason,
    string? Comment,
    IncomeStatusReason? Status = null);

/// <summary>
/// Compares each outgoing and income's budget amount (ADR-011, ADR-009) for a month with the month
/// before, and explains each difference (BUD-83, ADR-013). Annual bills are spread across every month,
/// so a difference is always a real change: something starting, ending, pausing, resuming, or an amount
/// change. Amounts are rounded to pennies before comparing, as they're shown.
/// </summary>
public static class MonthComparison
{
    /// <summary>The changes from the month before <paramref name="year"/>-<paramref name="month"/>, biggest effect first.</summary>
    public static IReadOnlyList<BudgetChange> Changes(
        IEnumerable<Outgoing> outgoings, IEnumerable<IncomeStream> income, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(outgoings);
        ArgumentNullException.ThrowIfNull(income);

        var firstOfMonth = new DateOnly(year, month, 1);
        var window = new Window(firstOfMonth.AddMonths(-1), firstOfMonth, firstOfMonth.AddMonths(1).AddDays(-1));

        var changes = new List<BudgetChange>();
        foreach (var outgoing in outgoings)
        {
            var before = Values.RoundForDisplay(outgoing.MonthlyEquivalentForMonth(window.Previous.Year, window.Previous.Month));
            var now = Values.RoundForDisplay(outgoing.MonthlyEquivalentForMonth(year, month));
            if (before != now)
            {
                var (reason, comment) = Explain(outgoing, before, now, window);
                changes.Add(new BudgetChange(false, outgoing.Id, outgoing.Name, before, now, before - now, reason, comment));
            }
        }

        foreach (var stream in income)
        {
            var before = Values.RoundForDisplay(stream.AmountForMonth(window.Previous.Year, window.Previous.Month));
            var now = Values.RoundForDisplay(stream.AmountForMonth(year, month));
            if (before != now)
            {
                var (reason, comment, status) = Explain(stream, before, now, window);
                changes.Add(new BudgetChange(true, stream.Id, stream.Name, before, now, now - before, reason, comment, status));
            }
        }

        return changes
            .OrderByDescending(c => Math.Abs(c.Effect))
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The previous month's first day, this month's first day and this month's last day.</summary>
    private readonly record struct Window(DateOnly Previous, DateOnly First, DateOnly Last)
    {
        /// <summary>Within the two months being compared.</summary>
        public bool Contains(DateOnly date) => date >= Previous && date <= Last;
    }

    private static (BudgetChangeReason, string?) Explain(Outgoing outgoing, decimal before, decimal now, Window window)
    {
        if (before == 0m && outgoing.StartsOn >= window.First)
        {
            return (BudgetChangeReason.New, null);
        }

        if (now == 0m && outgoing.EndsOn is { } ended && ended <= window.Last)
        {
            var end = outgoing.StatusPeriods.First(p => p.Reason == OutgoingStatusReason.Ended && p.StartDate == ended);
            return (BudgetChangeReason.Ended, end.Comment);
        }

        // An amount change in either month comes first: it's the most likely reason.
        var change = outgoing.AmountChanges
            .Where(c => window.Contains(c.EffectiveFrom) && c.EffectiveFrom > outgoing.StartsOn)
            .MaxBy(c => c.EffectiveFrom);
        if (change is not null)
        {
            return (BudgetChangeReason.AmountChanged, change.Comment);
        }

        var pause = outgoing.StatusPeriods
            .Where(p => p.Reason == OutgoingStatusReason.Paused && p.Overlaps(window.Previous, window.Last.AddDays(1)))
            .MaxBy(p => p.StartDate);
        if (pause is not null)
        {
            return (now < before ? BudgetChangeReason.Paused : BudgetChangeReason.Resumed, pause.Comment);
        }

        return (BudgetChangeReason.AmountChanged, outgoing.AmountChangeOn(window.Last)?.Comment);
    }

    private static (BudgetChangeReason, string?, IncomeStatusReason?) Explain(IncomeStream stream, decimal before, decimal now, Window window)
    {
        if (before == 0m && stream.StartsOn >= window.First)
        {
            return (BudgetChangeReason.New, null, null);
        }

        if (now < before && stream.EndsOn is { } ended && window.Contains(ended))
        {
            var end = stream.StatusPeriods.First(p => p.Reason == IncomeStatusReason.Ended && p.StartDate == ended);
            return (BudgetChangeReason.Ended, end.Comment, null);
        }

        var change = stream.AmountChanges
            .Where(c => window.Contains(c.EffectiveFrom) && c.EffectiveFrom > stream.StartsOn)
            .MaxBy(c => c.EffectiveFrom);
        if (change is not null)
        {
            return (BudgetChangeReason.AmountChanged, change.Comment, null);
        }

        var period = stream.StatusPeriods
            .Where(p => p.Reason != IncomeStatusReason.Ended && p.Overlaps(window.Previous, window.Last))
            .MaxBy(p => p.StartDate);
        if (period is not null)
        {
            return (now < before ? BudgetChangeReason.Reduced : BudgetChangeReason.BackToUsual, period.Comment, period.Reason);
        }

        return (BudgetChangeReason.AmountChanged, null, null);
    }
}
