using System.Globalization;
using Budget.Domain.Common;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Income;

/// <summary>Why a status period changes an income.</summary>
public enum IncomeStatusReason
{
    Holiday,
    SickLeave,
    UnpaidLeave,
    ParentalLeave,
    Ended,
    Other,
}

/// <summary>
/// From <see cref="EffectiveFrom"/>, <see cref="Amount"/> is received every <see cref="Every"/>
/// <see cref="Unit"/>s, e.g. Child Benefit every 4 weeks (BUD-57). The budget uses its
/// <see cref="MonthlyEquivalent"/>.
/// </summary>
public sealed class IncomeAmountChange : AuditableEntity
{
    private IncomeAmountChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid IncomeStreamId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>What's received each time, after tax and other deductions.</summary>
    public decimal Amount { get; private set; }

    public int Every { get; private set; } = 1;

    public PaymentUnit Unit { get; private set; } = PaymentUnit.Month;

    public string Comment { get; private set; } = string.Empty;

    public PaymentSchedule Schedule => new(Every, Unit);

    /// <summary>The amount averaged into a month (weekly × 52 ÷ 12, yearly ÷ 12). Unrounded.</summary>
    public decimal MonthlyEquivalent => Schedule.MonthlyEquivalent(Amount);

    internal static IncomeAmountChange Create(Guid incomeStreamId, DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment)
    {
        var change = new IncomeAmountChange { Id = Guid.CreateVersion7(), IncomeStreamId = incomeStreamId };
        change.Update(effectiveFrom, amount, schedule, comment);
        return change;
    }

    internal void Update(DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment)
    {
        var valid = schedule.Validated();
        EffectiveFrom = effectiveFrom;
        Amount = IncomeTimeline.ValidAmount(amount, nameof(amount));
        Every = valid.Every;
        Unit = valid.Unit;
        Comment = IncomeTimeline.RequiredText(comment, nameof(comment));
    }

    /// <summary>A plain description kept in the correction history, e.g. "102.20 every 4 weeks from 2026-04-06 (Child Benefit)".</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount:N2} {Schedule.Describe()} from {EffectiveFrom:yyyy-MM-dd} ({Comment})");
}

/// <summary>
/// Leave, a pause or the end of an income: from <see cref="StartDate"/> to <see cref="EndDate"/>
/// (or until further notice) the income is <see cref="ReducedMonthlyAmount"/> a month, or nothing.
/// </summary>
public sealed class IncomeStatusPeriod : AuditableEntity
{
    private IncomeStatusPeriod()
    {
    }

    public Guid Id { get; private set; }

    public Guid IncomeStreamId { get; private set; }

    public IncomeStatusReason Reason { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>The last day of the period (inclusive), or <c>null</c> for until further notice.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>The monthly amount during the period, e.g. statutory sick pay; <c>null</c> means no income.</summary>
    public decimal? ReducedMonthlyAmount { get; private set; }

    public string Comment { get; private set; } = string.Empty;

    public bool Covers(DateOnly date) => StartDate <= date && (EndDate is null || date <= EndDate);

    /// <summary>Whether this period shares at least one day with the given range.</summary>
    public bool Overlaps(DateOnly start, DateOnly? end) =>
        (end is null || StartDate <= end) && (EndDate is null || start <= EndDate);

    internal static IncomeStatusPeriod Create(
        Guid incomeStreamId, IncomeStatusReason reason, DateOnly startDate, DateOnly? endDate, decimal? reducedMonthlyAmount, string comment)
    {
        var period = new IncomeStatusPeriod { Id = Guid.CreateVersion7(), IncomeStreamId = incomeStreamId };
        period.Update(reason, startDate, endDate, reducedMonthlyAmount, comment);
        return period;
    }

    internal void Update(IncomeStatusReason reason, DateOnly startDate, DateOnly? endDate, decimal? reducedMonthlyAmount, string comment)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reason.");
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("The end date can't be before the start date.", nameof(endDate));
        }

        if (reason == IncomeStatusReason.Ended && (endDate is not null || reducedMonthlyAmount is not null))
        {
            throw new ArgumentException("An ended income has no end date or reduced amount.", nameof(reason));
        }

        Reason = reason;
        StartDate = startDate;
        EndDate = endDate;
        ReducedMonthlyAmount = reducedMonthlyAmount is { } amount ? IncomeTimeline.ValidAmount(amount, nameof(reducedMonthlyAmount)) : null;
        Comment = IncomeTimeline.RequiredText(comment, nameof(comment));
    }

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Reason} from {StartDate:yyyy-MM-dd}{(EndDate is { } end ? $" to {end:yyyy-MM-dd}" : " until further notice")}, "
        + $"{(ReducedMonthlyAmount is { } amount ? $"{amount:N2} a month" : "no income")} ({Comment})");
}

public enum IncomeEntryType
{
    AmountChange,
    StatusPeriod,
}

public enum IncomeCorrectionAction
{
    Edited,
    Deleted,
}

/// <summary>
/// A record that an amount change or status period was edited or deleted to fix a mistake: what it
/// was, what it became, and why. Who did it and when come from the audit fields.
/// </summary>
public sealed class IncomeCorrection : AuditableEntity
{
    public const int DescriptionMaxLength = 600;

    private IncomeCorrection()
    {
    }

    public Guid Id { get; private set; }

    public Guid IncomeStreamId { get; private set; }

    public IncomeEntryType EntryType { get; private set; }

    public Guid EntryId { get; private set; }

    public IncomeCorrectionAction Action { get; private set; }

    public string Before { get; private set; } = string.Empty;

    /// <summary>The corrected entry, or <c>null</c> when it was deleted.</summary>
    public string? After { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    internal static IncomeCorrection Create(
        Guid incomeStreamId, IncomeEntryType entryType, Guid entryId, IncomeCorrectionAction action, string before, string? after, string reason) => new()
    {
        Id = Guid.CreateVersion7(),
        IncomeStreamId = incomeStreamId,
        EntryType = entryType,
        EntryId = entryId,
        Action = action,
        Before = Truncate(before),
        After = after is null ? null : Truncate(after),
        Reason = IncomeTimeline.RequiredText(reason, nameof(reason)),
    };

    private static string Truncate(string text) => text.Length <= DescriptionMaxLength ? text : text[..DescriptionMaxLength];
}
