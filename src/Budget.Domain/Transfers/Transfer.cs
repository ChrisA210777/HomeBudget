using Budget.Domain.Common;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Transfers;

/// <summary>What can be changed about a transfer other than its amounts and pauses.</summary>
public sealed record TransferDetails
{
    /// <summary>Optional, e.g. "Bills top-up". Without one, it's shown as "From › To".</summary>
    public string? Name { get; init; }

    public required Guid FromAccountId { get; init; }

    public required Guid ToAccountId { get; init; }

    /// <summary>Anchors the schedule. Only used when it's created; later, correct the first amount instead.</summary>
    public required DateOnly FirstPaymentDate { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Money moved from one account (or pot) to another on a schedule (ADR-014, BUD-91). It has the same
/// effective-dated amount history, pauses and end dates as an outgoing (ADR-011) and the same payment-date
/// rules (ADR-013), but isn't spending: the money stays in the household.
/// </summary>
public sealed class Transfer : AuditableEntity, IHouseholdOwned
{
    public const int NameMaxLength = 100;
    public const int CommentMaxLength = Outgoing.CommentMaxLength;
    public const int NotesMaxLength = Outgoing.NotesMaxLength;
    public const int MaxPausedPayments = PaymentTimeline.MaxPausedPayments;

    private readonly List<TransferAmountChange> _amountChanges = [];
    private readonly List<TransferStatusPeriod> _statusPeriods = [];
    private readonly List<TransferCorrection> _corrections = [];

    private Transfer()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string? Name { get; private set; }

    public Guid FromAccountId { get; private set; }

    public Guid ToAccountId { get; private set; }

    public DateOnly FirstPaymentDate { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyCollection<TransferAmountChange> AmountChanges => _amountChanges.AsReadOnly();

    public IReadOnlyCollection<TransferStatusPeriod> StatusPeriods => _statusPeriods.AsReadOnly();

    public IReadOnlyCollection<TransferCorrection> Corrections => _corrections.AsReadOnly();

    public DateOnly StartsOn => Timeline.StartsOn;

    public DateOnly? EndsOn => Timeline.EndsOn;

    private PaymentTimeline Timeline => new(FirstPaymentDate, _amountChanges, _statusPeriods);

    public static Transfer Create(Guid householdId, TransferDetails details, decimal amount, PaymentSchedule schedule, string comment)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException("A transfer must belong to a household.", nameof(householdId));
        }

        ArgumentNullException.ThrowIfNull(details);

        var transfer = new Transfer { Id = Guid.CreateVersion7(), HouseholdId = householdId };
        transfer.UpdateDetails(details);
        transfer._amountChanges.Add(TransferAmountChange.Create(transfer.Id, details.FirstPaymentDate, amount, schedule, comment));
        return transfer;
    }

    public void UpdateDetails(TransferDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (details.FromAccountId == Guid.Empty || details.ToAccountId == Guid.Empty)
        {
            throw new ArgumentException("Choose the accounts it goes from and to.", nameof(details));
        }

        if (details.FromAccountId == details.ToAccountId)
        {
            throw new ArgumentException("A transfer has to go to a different account.", nameof(details));
        }

        Name = Values.Optional(details.Name, nameof(details.Name), NameMaxLength);
        FromAccountId = details.FromAccountId;
        ToAccountId = details.ToAccountId;
        if (_amountChanges.Count == 0)
        {
            FirstPaymentDate = details.FirstPaymentDate;
        }

        Notes = Values.Optional(details.Notes, nameof(details.Notes), NotesMaxLength);
    }

    // ---- Working out amounts and dates (shared with outgoings) -----------------------

    public bool IsActiveOn(DateOnly date) => Timeline.IsActiveOn(date);

    public TransferAmountChange? AmountChangeOn(DateOnly date) =>
        _amountChanges.Where(c => c.EffectiveFrom <= date).MaxBy(c => c.EffectiveFrom);

    public decimal MonthlyEquivalentOn(DateOnly date) => Timeline.MonthlyEquivalentOn(date);

    /// <summary>The budget average for a month (ADR-011), as for outgoings. Unrounded.</summary>
    public decimal MonthlyEquivalentForMonth(int year, int month) => Timeline.MonthlyEquivalentForMonth(year, month);

    public bool IsPaymentDate(DateOnly date) => Timeline.IsPaymentDate(date);

    public DateOnly? NextPaymentOnOrAfter(DateOnly date) => Timeline.NextPaymentOnOrAfter(date);

    public IReadOnlyList<ScheduledPayment> PaymentsBetween(DateOnly from, DateOnly to) => Timeline.PaymentsBetween(from, to);

    public DateOnly ResumesAfterSkipping(DateOnly firstSkipped, int payments) => Timeline.ResumesAfterSkipping(firstSkipped, payments);

    public int? PaymentsSkippedBy(TransferStatusPeriod period) => Timeline.PaymentsSkippedBy(period);

    public IReadOnlyList<DateOnly> PaymentDatesFrom(DateOnly from, int count) => Timeline.PaymentDatesFrom(from, count);

    // ---- Amount changes ---------------------------------------------------------

    public TransferAmountChange ChangeAmount(DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment)
    {
        EnsureNoAmountChangeOn(effectiveFrom, exceptId: null);
        var change = TransferAmountChange.Create(Id, effectiveFrom, amount, schedule, comment);
        _amountChanges.Add(change);
        return change;
    }

    public void CorrectAmountChange(Guid changeId, DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment, string reason)
    {
        var change = FindAmountChange(changeId);
        EnsureNoAmountChangeOn(effectiveFrom, exceptId: changeId);

        var isFirst = change.EffectiveFrom == StartsOn;
        var others = _amountChanges.Where(c => c.Id != changeId).Select(c => c.EffectiveFrom).ToList();
        if (isFirst && others.Count > 0 && effectiveFrom > others.Min())
        {
            throw new InvalidOperationException("The first payment has to be before any later amount changes.");
        }

        if (!isFirst && effectiveFrom <= StartsOn)
        {
            throw new InvalidOperationException("An amount change has to be after the first payment.");
        }

        var before = change.Describe();
        change.Update(effectiveFrom, amount, schedule, comment);
        if (isFirst)
        {
            FirstPaymentDate = effectiveFrom;
        }

        _corrections.Add(TransferCorrection.Create(Id, OutgoingEntryType.AmountChange, changeId, OutgoingCorrectionAction.Edited, before, change.Describe(), reason));
    }

    public void DeleteAmountChange(Guid changeId, string reason)
    {
        var change = FindAmountChange(changeId);
        if (change.EffectiveFrom == StartsOn)
        {
            throw new InvalidOperationException("The first amount can't be deleted. Change it, or delete the transfer instead.");
        }

        _corrections.Add(TransferCorrection.Create(Id, OutgoingEntryType.AmountChange, changeId, OutgoingCorrectionAction.Deleted, change.Describe(), null, reason));
        _amountChanges.Remove(change);
    }

    // ---- Pauses and ending ----------------------------------------------------------

    public TransferStatusPeriod Pause(DateOnly from, DateOnly? resumesOn, string comment)
    {
        Timeline.EnsurePausedOnPaymentDates(from, resumesOn);
        return AddStatusPeriod(OutgoingStatusReason.Paused, from, resumesOn, comment);
    }

    public TransferStatusPeriod PauseFor(DateOnly firstSkipped, int payments, string comment)
    {
        if (payments is < 1 or > MaxPausedPayments)
        {
            throw new ArgumentOutOfRangeException(nameof(payments), $"A pause skips from 1 to {MaxPausedPayments} payments.");
        }

        Timeline.EnsurePausedOnPaymentDates(firstSkipped, resumesOn: null);
        return Pause(firstSkipped, ResumesAfterSkipping(firstSkipped, payments), comment);
    }

    public TransferStatusPeriod End(DateOnly from, string comment) =>
        AddStatusPeriod(OutgoingStatusReason.Ended, from, resumesOn: null, comment);

    public void CorrectStatusPeriod(Guid periodId, OutgoingStatusReason reason, DateOnly startDate, DateOnly? resumesOn, string comment, string correctionReason)
    {
        var period = FindStatusPeriod(periodId);
        if (reason == OutgoingStatusReason.Paused)
        {
            Timeline.EnsurePausedOnPaymentDates(startDate, resumesOn);
        }

        EnsureNoOverlap(startDate, resumesOn, exceptId: periodId);

        var before = period.Describe();
        period.Update(reason, startDate, resumesOn, comment);
        _corrections.Add(TransferCorrection.Create(Id, OutgoingEntryType.StatusPeriod, periodId, OutgoingCorrectionAction.Edited, before, period.Describe(), correctionReason));
    }

    public void DeleteStatusPeriod(Guid periodId, string reason)
    {
        var period = FindStatusPeriod(periodId);
        _corrections.Add(TransferCorrection.Create(Id, OutgoingEntryType.StatusPeriod, periodId, OutgoingCorrectionAction.Deleted, period.Describe(), null, reason));
        _statusPeriods.Remove(period);
    }

    /// <summary>Brings back a correction record from an exported file. It doesn't change any entry.</summary>
    public TransferCorrection RestoreCorrection(OutgoingEntryType entryType, Guid entryId, OutgoingCorrectionAction action, string before, string? after, string reason)
    {
        if (!Enum.IsDefined(entryType) || !Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(entryType), "Unknown kind of correction.");
        }

        var correction = TransferCorrection.Create(Id, entryType, entryId, action, before, after, reason);
        _corrections.Add(correction);
        return correction;
    }

    private TransferStatusPeriod AddStatusPeriod(OutgoingStatusReason reason, DateOnly start, DateOnly? resumesOn, string comment)
    {
        EnsureNoOverlap(start, resumesOn, exceptId: null);
        var period = TransferStatusPeriod.Create(Id, reason, start, resumesOn, comment);
        _statusPeriods.Add(period);
        return period;
    }

    private TransferAmountChange FindAmountChange(Guid id) =>
        _amountChanges.SingleOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("That amount change doesn't exist.");

    private TransferStatusPeriod FindStatusPeriod(Guid id) =>
        _statusPeriods.SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("That pause doesn't exist.");

    private void EnsureNoAmountChangeOn(DateOnly date, Guid? exceptId)
    {
        if (_amountChanges.Any(c => c.EffectiveFrom == date && c.Id != exceptId))
        {
            throw new InvalidOperationException("There's already an amount change on that date. Change that one instead.");
        }
    }

    private void EnsureNoOverlap(DateOnly start, DateOnly? resumesOn, Guid? exceptId)
    {
        if (_statusPeriods.Any(p => p.Id != exceptId && p.Overlaps(start, resumesOn)))
        {
            throw new InvalidOperationException("That overlaps another pause or the end of this transfer.");
        }
    }
}
