using System.Globalization;
using Budget.Domain.Common;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Transfers;

/// <summary>
/// From <see cref="EffectiveFrom"/>, the transfer moves <see cref="Amount"/> paid on
/// <see cref="Schedule"/> (e.g. £480 a year). Keeping the schedule with each change means
/// switching from monthly to annual payments doesn't rewrite earlier months.
/// </summary>
public sealed class TransferAmountChange : AuditableEntity, IScheduledAmount
{
    private TransferAmountChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid TransferId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public decimal Amount { get; private set; }

    public int Every { get; private set; }

    public PaymentUnit Unit { get; private set; }

    public string Comment { get; private set; } = string.Empty;

    public PaymentSchedule Schedule => new(Every, Unit);

    public decimal MonthlyEquivalent => Schedule.MonthlyEquivalent(Amount);

    internal static TransferAmountChange Create(Guid transferId, DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment)
    {
        var change = new TransferAmountChange { Id = Guid.CreateVersion7(), TransferId = transferId };
        change.Update(effectiveFrom, amount, schedule, comment);
        return change;
    }

    internal void Update(DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment)
    {
        var valid = schedule.Validated();
        EffectiveFrom = effectiveFrom;
        Amount = Values.Amount(amount, nameof(amount));
        Every = valid.Every;
        Unit = valid.Unit;
        Comment = Values.Required(comment, nameof(comment), Transfer.CommentMaxLength);
    }

    /// <summary>e.g. "£480.00 a year from 2026-11-01 (Renewal)".</summary>
    internal string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"£{Amount:N2} {Schedule.Describe()} from {EffectiveFrom:yyyy-MM-dd} ({Comment})");
}

/// <summary>
/// A pause (e.g. a mortgage payment holiday), optionally with the date payments resume, or the
/// date the transfer ended. Nothing is due while it applies.
/// </summary>
public sealed class TransferStatusPeriod : AuditableEntity, IScheduledPause
{
    private TransferStatusPeriod()
    {
    }

    public Guid Id { get; private set; }

    public Guid TransferId { get; private set; }

    public OutgoingStatusReason Reason { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>When payments start again (not included in the pause). Null: until further notice. Always null when ended.</summary>
    public DateOnly? ResumesOn { get; private set; }

    public string Comment { get; private set; } = string.Empty;

    public bool Covers(DateOnly date) => date >= StartDate && (ResumesOn is null || date < ResumesOn);

    public bool Overlaps(DateOnly start, DateOnly? resumesOn) =>
        start < (ResumesOn ?? DateOnly.MaxValue) && StartDate < (resumesOn ?? DateOnly.MaxValue);

    internal static TransferStatusPeriod Create(Guid transferId, OutgoingStatusReason reason, DateOnly startDate, DateOnly? resumesOn, string comment)
    {
        var period = new TransferStatusPeriod { Id = Guid.CreateVersion7(), TransferId = transferId };
        period.Update(reason, startDate, resumesOn, comment);
        return period;
    }

    internal void Update(OutgoingStatusReason reason, DateOnly startDate, DateOnly? resumesOn, string comment)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reason.");
        }

        if (reason == OutgoingStatusReason.Ended && resumesOn is not null)
        {
            throw new InvalidOperationException("An ended transfer doesn't resume. Use a pause instead.");
        }

        if (resumesOn <= startDate)
        {
            throw new InvalidOperationException("Payments must resume after the pause starts.");
        }

        Reason = reason;
        StartDate = startDate;
        ResumesOn = resumesOn;
        Comment = Values.Required(comment, nameof(comment), Transfer.CommentMaxLength);
    }

    /// <summary>e.g. "Paused 2026-12-01 until 2027-03-01 (Payment holiday)".</summary>
    internal string Describe() => Reason == OutgoingStatusReason.Ended
        ? string.Create(CultureInfo.InvariantCulture, $"Ended from {StartDate:yyyy-MM-dd} ({Comment})")
        : string.Create(CultureInfo.InvariantCulture, $"Paused {StartDate:yyyy-MM-dd} {(ResumesOn is { } r ? $"until {r:yyyy-MM-dd}" : "until further notice")} ({Comment})");
}

/// <summary>A record of an amount change or pause being corrected or deleted, keeping the original.</summary>
public sealed class TransferCorrection : AuditableEntity
{
    public const int DescriptionMaxLength = 600;

    private TransferCorrection()
    {
    }

    public Guid Id { get; private set; }

    public Guid TransferId { get; private set; }

    public OutgoingEntryType EntryType { get; private set; }

    public Guid EntryId { get; private set; }

    public OutgoingCorrectionAction Action { get; private set; }

    public string Before { get; private set; } = string.Empty;

    public string? After { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    internal static TransferCorrection Create(
        Guid transferId, OutgoingEntryType entryType, Guid entryId, OutgoingCorrectionAction action, string before, string? after, string reason) => new()
    {
        Id = Guid.CreateVersion7(),
        TransferId = transferId,
        EntryType = entryType,
        EntryId = entryId,
        Action = action,
        Before = Truncate(before),
        After = after is null ? null : Truncate(after),
        Reason = Values.Required(reason, nameof(reason), Transfer.CommentMaxLength),
    };

    private static string Truncate(string text) => text.Length <= DescriptionMaxLength ? text : text[..DescriptionMaxLength];
}
