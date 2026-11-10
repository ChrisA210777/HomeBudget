using Budget.Domain.Common;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Income;

/// <summary>
/// A source of money coming into a household, belonging to a person and recorded as a timeline of
/// monthly net amounts and status periods (see ADR-009). Mistakes are corrected with a reason,
/// and the original is kept as an <see cref="IncomeCorrection"/>.
/// </summary>
/// <remarks>Its amount changes, status periods and corrections must be loaded to change them.</remarks>
public sealed class IncomeStream : AuditableEntity, IHouseholdOwned
{
    public const int NameMaxLength = 100;
    public const int CommentMaxLength = 200;
    public const decimal MaxAmount = 10_000_000m;

    private readonly List<IncomeAmountChange> _amountChanges = [];
    private readonly List<IncomeStatusPeriod> _statusPeriods = [];
    private readonly List<IncomeCorrection> _corrections = [];

    private IncomeStream()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid PersonId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The account or pot it's paid into (BUD-90, ADR-014), in the same household.</summary>
    public Guid? AccountId { get; private set; }

    public IReadOnlyCollection<IncomeAmountChange> AmountChanges => _amountChanges.AsReadOnly();

    public IReadOnlyCollection<IncomeStatusPeriod> StatusPeriods => _statusPeriods.AsReadOnly();

    public IReadOnlyCollection<IncomeCorrection> Corrections => _corrections.AsReadOnly();

    /// <summary>The date of the first amount, i.e. when the income starts.</summary>
    public DateOnly StartsOn => _amountChanges.Min(c => c.EffectiveFrom);

    /// <summary>The start of an "Ended" period, if the income has ended.</summary>
    public DateOnly? EndsOn => _statusPeriods.Where(p => p.Reason == IncomeStatusReason.Ended).Select(p => (DateOnly?)p.StartDate).Min();

    public static IncomeStream Create(
        Guid householdId, Guid personId, string name, decimal amount, DateOnly startsOn, string comment, PaymentSchedule? schedule = null)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException("An income must belong to a household.", nameof(householdId));
        }

        var stream = new IncomeStream { Id = Guid.CreateVersion7(), HouseholdId = householdId };
        stream.Rename(name);
        stream.ChangePerson(personId);
        stream._amountChanges.Add(IncomeAmountChange.Create(stream.Id, startsOn, amount, schedule ?? PaymentSchedule.Monthly, comment));
        return stream;
    }

    public void Rename(string name) =>
        Name = IncomeTimeline.RequiredText(name, nameof(name), NameMaxLength);

    /// <summary>The account or pot it's paid into, or <c>null</c> if not recorded.</summary>
    public void PayInto(Guid? accountId) => AccountId = accountId;

    public void ChangePerson(Guid personId)
    {
        if (personId == Guid.Empty)
        {
            throw new ArgumentException("An income must belong to a person.", nameof(personId));
        }

        PersonId = personId;
    }

    public decimal AmountOn(DateOnly date) => IncomeTimeline.AmountOn(_amountChanges, _statusPeriods, date);

    public decimal AmountForMonth(int year, int month) => IncomeTimeline.AmountForMonth(_amountChanges, _statusPeriods, year, month);

    // Amount changes

    public IncomeAmountChange ChangeAmount(DateOnly effectiveFrom, decimal amount, string comment, PaymentSchedule? schedule = null)
    {
        EnsureNoAmountChangeOn(effectiveFrom, exceptId: null);
        var change = IncomeAmountChange.Create(Id, effectiveFrom, amount, schedule ?? PaymentSchedule.Monthly, comment);
        _amountChanges.Add(change);
        return change;
    }

    public void CorrectAmountChange(Guid changeId, DateOnly effectiveFrom, decimal amount, string comment, string reason, PaymentSchedule? schedule = null)
    {
        var change = FindAmountChange(changeId);
        EnsureNoAmountChangeOn(effectiveFrom, exceptId: changeId);

        var before = change.Describe();
        change.Update(effectiveFrom, amount, schedule ?? PaymentSchedule.Monthly, comment);
        _corrections.Add(IncomeCorrection.Create(Id, IncomeEntryType.AmountChange, changeId, IncomeCorrectionAction.Edited, before, change.Describe(), reason));
    }

    /// <summary>Deletes an amount change made by mistake. The first amount can't be deleted: delete the income instead.</summary>
    public void DeleteAmountChange(Guid changeId, string reason)
    {
        var change = FindAmountChange(changeId);
        if (change.EffectiveFrom == StartsOn)
        {
            throw new InvalidOperationException("The first amount can't be deleted. Change it, or delete the income instead.");
        }

        _corrections.Add(IncomeCorrection.Create(Id, IncomeEntryType.AmountChange, changeId, IncomeCorrectionAction.Deleted, change.Describe(), null, reason));
        _amountChanges.Remove(change);
    }

    // Status periods

    public IncomeStatusPeriod AddStatusPeriod(
        IncomeStatusReason reason, DateOnly startDate, DateOnly? endDate, decimal? reducedMonthlyAmount, string comment)
    {
        EnsureNoOverlap(startDate, endDate, exceptId: null);
        var period = IncomeStatusPeriod.Create(Id, reason, startDate, endDate, reducedMonthlyAmount, comment);
        _statusPeriods.Add(period);
        return period;
    }

    /// <summary>Ends the income from a date, until further notice.</summary>
    public IncomeStatusPeriod End(DateOnly from, string comment) =>
        AddStatusPeriod(IncomeStatusReason.Ended, from, endDate: null, reducedMonthlyAmount: null, comment);

    public void CorrectStatusPeriod(
        Guid periodId, IncomeStatusReason reason, DateOnly startDate, DateOnly? endDate, decimal? reducedMonthlyAmount, string comment, string correctionReason)
    {
        var period = FindStatusPeriod(periodId);
        EnsureNoOverlap(startDate, endDate, exceptId: periodId);

        var before = period.Describe();
        period.Update(reason, startDate, endDate, reducedMonthlyAmount, comment);
        _corrections.Add(IncomeCorrection.Create(Id, IncomeEntryType.StatusPeriod, periodId, IncomeCorrectionAction.Edited, before, period.Describe(), correctionReason));
    }

    public void DeleteStatusPeriod(Guid periodId, string reason)
    {
        var period = FindStatusPeriod(periodId);
        _corrections.Add(IncomeCorrection.Create(Id, IncomeEntryType.StatusPeriod, periodId, IncomeCorrectionAction.Deleted, period.Describe(), null, reason));
        _statusPeriods.Remove(period);
    }

    /// <summary>
    /// Brings back a correction record from an exported file (BUD-61): what an entry was, what it became, and why.
    /// It doesn't change any entry; the file already holds the corrected entries.
    /// </summary>
    public IncomeCorrection RestoreCorrection(IncomeEntryType entryType, Guid entryId, IncomeCorrectionAction action, string before, string? after, string reason)
    {
        if (!Enum.IsDefined(entryType) || !Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(entryType), "Unknown kind of correction.");
        }

        var correction = IncomeCorrection.Create(Id, entryType, entryId, action, before, after, reason);
        _corrections.Add(correction);
        return correction;
    }

    private IncomeAmountChange FindAmountChange(Guid id) =>
        _amountChanges.SingleOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("That amount change doesn't exist.");

    private IncomeStatusPeriod FindStatusPeriod(Guid id) =>
        _statusPeriods.SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("That period doesn't exist.");

    private void EnsureNoAmountChangeOn(DateOnly date, Guid? exceptId)
    {
        if (_amountChanges.Any(c => c.EffectiveFrom == date && c.Id != exceptId))
        {
            throw new InvalidOperationException("There's already an amount change on that date. Change that one instead.");
        }
    }

    private void EnsureNoOverlap(DateOnly start, DateOnly? end, Guid? exceptId)
    {
        if (_statusPeriods.Any(p => p.Id != exceptId && p.Overlaps(start, end)))
        {
            throw new InvalidOperationException("That overlaps another leave or pause for this income.");
        }
    }
}
