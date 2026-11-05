using Budget.Domain.Common;
using Budget.Domain.Households;

namespace Budget.Domain.Outgoings;

/// <summary>Everything about an outgoing except its amounts and pauses, as entered on the form.</summary>
public sealed record OutgoingDetails
{
    public required string Name { get; init; }

    public OutgoingKind Kind { get; init; }

    public required Guid CategoryId { get; init; }

    public OutgoingPriority Priority { get; init; }

    /// <summary>Who it's for; <c>null</c> for the whole household.</summary>
    public Guid? PersonId { get; init; }

    /// <summary>When payments start; later payments fall on the schedule from this date.</summary>
    public required DateOnly FirstPaymentDate { get; init; }

    /// <summary>The (first) renewal or anniversary date, if it renews.</summary>
    public DateOnly? RenewalDate { get; init; }

    public int RenewalEveryMonths { get; init; } = 12;

    public bool AutoRenews { get; init; }

    public AlertPeriod AlertPeriod { get; init; }

    /// <summary>Who it's paid to, or the savings pot it goes into.</summary>
    public string? Recipient { get; init; }

    public PaymentMethod? PaymentMethod { get; init; }

    /// <summary>The name of the account it's paid from, e.g. "Joint current account".</summary>
    public string? PaidFrom { get; init; }

    /// <summary>What the reference is called, e.g. "Policy number".</summary>
    public string? ReferenceLabel { get; init; }

    public string? ReferenceValue { get; init; }

    public string? Website { get; init; }

    public string? ContactPhone { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// A recurring bill, savings amount or spending allowance (ADR-011). Its amounts are a dated history,
/// and it can be paused or ended. Every outgoing is averaged into a monthly amount for the budget.
/// Mistakes are corrected with a reason, keeping the original as an <see cref="OutgoingCorrection"/>.
/// </summary>
/// <remarks>Its amount changes, status periods and corrections must be loaded to change them.</remarks>
public sealed class Outgoing : AuditableEntity, IHouseholdOwned
{
    public const int NameMaxLength = 100;
    public const int CommentMaxLength = 200;
    public const int RecipientMaxLength = 100;
    public const int PaidFromMaxLength = 100;
    public const int ReferenceLabelMaxLength = 50;
    public const int ReferenceValueMaxLength = 100;
    public const int WebsiteMaxLength = 300;
    public const int ContactPhoneMaxLength = 30;
    public const int NotesMaxLength = 2000;
    public const int MaxRenewalEveryMonths = 120;
    public const int MaxPausedPayments = 520;

    private readonly List<OutgoingAmountChange> _amountChanges = [];
    private readonly List<OutgoingStatusPeriod> _statusPeriods = [];
    private readonly List<OutgoingCorrection> _corrections = [];

    private Outgoing()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public OutgoingKind Kind { get; private set; }

    public Guid CategoryId { get; private set; }

    public OutgoingPriority Priority { get; private set; }

    public Guid? PersonId { get; private set; }

    public DateOnly FirstPaymentDate { get; private set; }

    public DateOnly? RenewalDate { get; private set; }

    public int RenewalEveryMonths { get; private set; } = 12;

    public bool AutoRenews { get; private set; }

    public AlertPeriod AlertPeriod { get; private set; }

    public string? Recipient { get; private set; }

    public PaymentMethod? PaymentMethod { get; private set; }

    public string? PaidFrom { get; private set; }

    public string? ReferenceLabel { get; private set; }

    public string? ReferenceValue { get; private set; }

    public string? Website { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyCollection<OutgoingAmountChange> AmountChanges => _amountChanges.AsReadOnly();

    public IReadOnlyCollection<OutgoingStatusPeriod> StatusPeriods => _statusPeriods.AsReadOnly();

    public IReadOnlyCollection<OutgoingCorrection> Corrections => _corrections.AsReadOnly();

    /// <summary>When the first amount applies from.</summary>
    public DateOnly StartsOn => _amountChanges.Min(c => c.EffectiveFrom);

    /// <summary>When it ended, if it has.</summary>
    public DateOnly? EndsOn => _statusPeriods.Where(p => p.Reason == OutgoingStatusReason.Ended).Select(p => (DateOnly?)p.StartDate).Min();

    public static Outgoing Create(Guid householdId, OutgoingDetails details, decimal amount, PaymentSchedule schedule, string comment)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException("An outgoing must belong to a household.", nameof(householdId));
        }

        ArgumentNullException.ThrowIfNull(details);

        var outgoing = new Outgoing { Id = Guid.CreateVersion7(), HouseholdId = householdId };
        outgoing.UpdateDetails(details);
        outgoing._amountChanges.Add(OutgoingAmountChange.Create(outgoing.Id, details.FirstPaymentDate, amount, schedule, comment));
        return outgoing;
    }

    public void UpdateDetails(OutgoingDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!Enum.IsDefined(details.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.Kind, "Unknown kind.");
        }

        if (!Enum.IsDefined(details.Priority))
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.Priority, "Unknown priority.");
        }

        if (!Enum.IsDefined(details.AlertPeriod))
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.AlertPeriod, "Unknown alert period.");
        }

        if (details.PaymentMethod is { } method && !Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(details), method, "Unknown payment method.");
        }

        if (details.CategoryId == Guid.Empty)
        {
            throw new ArgumentException("Choose a category.", nameof(details));
        }

        if (details.RenewalEveryMonths is < 1 or > MaxRenewalEveryMonths)
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.RenewalEveryMonths, $"Renewals must be every 1 to {MaxRenewalEveryMonths} months.");
        }

        if (details.Kind == OutgoingKind.Allowance && details.RenewalDate is not null)
        {
            throw new InvalidOperationException("Allowances don't renew.");
        }

        Name = Values.Required(details.Name, nameof(details.Name), NameMaxLength);
        Kind = details.Kind;
        CategoryId = details.CategoryId;
        Priority = details.Priority;
        PersonId = details.PersonId == Guid.Empty ? null : details.PersonId;
        FirstPaymentDate = details.FirstPaymentDate;
        RenewalDate = details.RenewalDate;
        RenewalEveryMonths = details.RenewalEveryMonths;
        AutoRenews = details.RenewalDate is not null && details.AutoRenews;
        AlertPeriod = details.RenewalDate is null ? AlertPeriod.None : details.AlertPeriod;
        Recipient = Values.Optional(details.Recipient, nameof(details.Recipient), RecipientMaxLength);
        PaymentMethod = details.PaymentMethod;
        PaidFrom = Values.Optional(details.PaidFrom, nameof(details.PaidFrom), PaidFromMaxLength);
        ReferenceLabel = Values.Optional(details.ReferenceLabel, nameof(details.ReferenceLabel), ReferenceLabelMaxLength);
        ReferenceValue = Values.Optional(details.ReferenceValue, nameof(details.ReferenceValue), ReferenceValueMaxLength);
        Website = ValidWebsite(details.Website);
        ContactPhone = Values.Optional(details.ContactPhone, nameof(details.ContactPhone), ContactPhoneMaxLength);
        Notes = Values.Optional(details.Notes, nameof(details.Notes), NotesMaxLength);
    }

    // ---- Working out amounts and dates ------------------------------------------

    /// <summary>Whether it's being paid on a date: started, and not paused or ended.</summary>
    public bool IsActiveOn(DateOnly date) =>
        _amountChanges.Any(c => c.EffectiveFrom <= date) && !_statusPeriods.Any(p => p.Covers(date));

    /// <summary>The amount change in force on a date (ignoring pauses), if it has started.</summary>
    public OutgoingAmountChange? AmountChangeOn(DateOnly date) =>
        _amountChanges.Where(c => c.EffectiveFrom <= date).MaxBy(c => c.EffectiveFrom);

    /// <summary>The monthly equivalent on a date: 0 before it starts and while paused or ended.</summary>
    public decimal MonthlyEquivalentOn(DateOnly date) =>
        _statusPeriods.Any(p => p.Covers(date)) ? 0m : AmountChangeOn(date)?.MonthlyEquivalent ?? 0m;

    /// <summary>
    /// The monthly equivalent for a calendar month (ADR-011). Unrounded.
    /// <para>Each month is judged on the payments due in it (see <see cref="CountingDaysIn"/>): a payment
    /// that's paused or after the end counts nothing, and the month it starts in counts in full. So a
    /// monthly or yearly outgoing counts in full or not at all, and a weekly one loses the share of the
    /// month's payments that are paused or after the end.</para>
    /// </summary>
    public decimal MonthlyEquivalentForMonth(int year, int month)
    {
        var days = CountingDaysIn(year, month).Where(d => d >= StartsOn).ToList();
        return days.Count == 0 ? 0m : days.Sum(MonthlyEquivalentOn) / days.Count;
    }

    /// <summary>
    /// The days that decide a month's amount. For a weekly schedule, its payment dates in the month
    /// (or, when payments are further apart than a month, the latest one before the month ends). For
    /// monthly or longer schedules, one day each month on the first payment's day of the month, because
    /// those are averaged into every month.
    /// </summary>
    public IReadOnlyList<DateOnly> CountingDaysIn(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var schedule = (AmountChangeOn(last) ?? _amountChanges.MinBy(c => c.EffectiveFrom)!).Schedule;

        if (schedule.Unit != PaymentUnit.Week)
        {
            return [new DateOnly(year, month, Math.Min(FirstPaymentDate.Day, DateTime.DaysInMonth(year, month)))];
        }

        var period = 7 * schedule.Every;
        var dates = new List<DateOnly>();
        var next = schedule.NextOnOrAfter(FirstPaymentDate, first);
        for (var date = next; date <= last; date = date.AddDays(period))
        {
            dates.Add(date);
        }

        if (dates.Count == 0 && next > FirstPaymentDate)
        {
            dates.Add(next.AddDays(-period));
        }

        return dates;
    }

    /// <summary>Whether a payment is due on a date under the schedule in force then (ignoring pauses).</summary>
    public bool IsPaymentDate(DateOnly date) =>
        date >= StartsOn && AmountChangeOn(date)!.Schedule.NextOnOrAfter(FirstPaymentDate, date) == date;

    /// <summary>The payment after <paramref name="date"/>, ignoring pauses.</summary>
    public DateOnly PaymentAfter(DateOnly date)
    {
        var from = date < StartsOn ? StartsOn : date.AddDays(1);
        var schedule = (AmountChangeOn(from) ?? _amountChanges.MinBy(c => c.EffectiveFrom)!).Schedule;
        return schedule.NextOnOrAfter(FirstPaymentDate, from);
    }

    /// <summary>The next payment on or after a date, or <c>null</c> if none is due (ended, or paused until further notice).</summary>
    public DateOnly? NextPaymentOnOrAfter(DateOnly date)
    {
        var from = date < StartsOn ? StartsOn : date;
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var schedule = (AmountChangeOn(from) ?? _amountChanges.MinBy(c => c.EffectiveFrom)!).Schedule;
            var next = schedule.NextOnOrAfter(FirstPaymentDate, from);
            var blocking = _statusPeriods.FirstOrDefault(p => p.Covers(next));
            if (blocking is null)
            {
                return next;
            }

            if (blocking.ResumesOn is not { } resumes)
            {
                return null;
            }

            from = resumes;
        }

        return null;
    }

    /// <summary>The next renewal on or after a date, rolling the renewal date forward by the renewal cycle.</summary>
    public DateOnly? NextRenewalOnOrAfter(DateOnly date) =>
        RenewalDate is { } renewal ? Dates.NextMonthlyOnOrAfter(renewal, RenewalEveryMonths, date) : null;

    /// <summary>
    /// Whether to highlight it as coming up for renewal: it has an alert period, is active, and
    /// <paramref name="today"/> is within the alert period before the next renewal.
    /// </summary>
    public bool IsComingUp(DateOnly today) =>
        AlertPeriod != AlertPeriod.None
        && IsActiveOn(today)
        && NextRenewalOnOrAfter(today) is { } next
        && today >= Dates.Subtract(next, AlertPeriod);

    // ---- Amount changes ---------------------------------------------------------

    public OutgoingAmountChange ChangeAmount(DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment)
    {
        EnsureNoAmountChangeOn(effectiveFrom, exceptId: null);
        var change = OutgoingAmountChange.Create(Id, effectiveFrom, amount, schedule, comment);
        _amountChanges.Add(change);
        return change;
    }

    public void CorrectAmountChange(Guid changeId, DateOnly effectiveFrom, decimal amount, PaymentSchedule schedule, string comment, string reason)
    {
        var change = FindAmountChange(changeId);
        EnsureNoAmountChangeOn(effectiveFrom, exceptId: changeId);

        var before = change.Describe();
        change.Update(effectiveFrom, amount, schedule, comment);
        _corrections.Add(OutgoingCorrection.Create(Id, OutgoingEntryType.AmountChange, changeId, OutgoingCorrectionAction.Edited, before, change.Describe(), reason));
    }

    /// <summary>Deletes an amount change made by mistake. The first amount can't be deleted: delete the outgoing instead.</summary>
    public void DeleteAmountChange(Guid changeId, string reason)
    {
        var change = FindAmountChange(changeId);
        if (change.EffectiveFrom == StartsOn)
        {
            throw new InvalidOperationException("The first amount can't be deleted. Change it, or delete the outgoing instead.");
        }

        _corrections.Add(OutgoingCorrection.Create(Id, OutgoingEntryType.AmountChange, changeId, OutgoingCorrectionAction.Deleted, change.Describe(), null, reason));
        _amountChanges.Remove(change);
    }

    // ---- Pauses and ending ----------------------------------------------------------

    /// <summary>
    /// Pauses whole payments: from the payment on <paramref name="from"/> until the payment on
    /// <paramref name="resumesOn"/> (or until further notice). Both must be payment dates.
    /// </summary>
    public OutgoingStatusPeriod Pause(DateOnly from, DateOnly? resumesOn, string comment)
    {
        EnsurePausedOnPaymentDates(from, resumesOn);
        return AddStatusPeriod(OutgoingStatusReason.Paused, from, resumesOn, comment);
    }

    /// <summary>Skips <paramref name="payments"/> payments, starting with the one on <paramref name="firstSkipped"/>.</summary>
    public OutgoingStatusPeriod PauseFor(DateOnly firstSkipped, int payments, string comment)
    {
        if (payments is < 1 or > MaxPausedPayments)
        {
            throw new ArgumentOutOfRangeException(nameof(payments), $"A pause skips from 1 to {MaxPausedPayments} payments.");
        }

        EnsurePausedOnPaymentDates(firstSkipped, resumesOn: null);
        var resumesOn = firstSkipped;
        for (var i = 0; i < payments; i++)
        {
            resumesOn = PaymentAfter(resumesOn);
        }

        return Pause(firstSkipped, resumesOn, comment);
    }

    /// <summary>Stops the outgoing from a date. It stays in the history for earlier months.</summary>
    public OutgoingStatusPeriod End(DateOnly from, string comment) =>
        AddStatusPeriod(OutgoingStatusReason.Ended, from, resumesOn: null, comment);

    public void CorrectStatusPeriod(Guid periodId, OutgoingStatusReason reason, DateOnly startDate, DateOnly? resumesOn, string comment, string correctionReason)
    {
        var period = FindStatusPeriod(periodId);
        if (reason == OutgoingStatusReason.Paused)
        {
            EnsurePausedOnPaymentDates(startDate, resumesOn);
        }

        EnsureNoOverlap(startDate, resumesOn, exceptId: periodId);

        var before = period.Describe();
        period.Update(reason, startDate, resumesOn, comment);
        _corrections.Add(OutgoingCorrection.Create(Id, OutgoingEntryType.StatusPeriod, periodId, OutgoingCorrectionAction.Edited, before, period.Describe(), correctionReason));
    }

    public void DeleteStatusPeriod(Guid periodId, string reason)
    {
        var period = FindStatusPeriod(periodId);
        _corrections.Add(OutgoingCorrection.Create(Id, OutgoingEntryType.StatusPeriod, periodId, OutgoingCorrectionAction.Deleted, period.Describe(), null, reason));
        _statusPeriods.Remove(period);
    }

    private OutgoingStatusPeriod AddStatusPeriod(OutgoingStatusReason reason, DateOnly start, DateOnly? resumesOn, string comment)
    {
        EnsureNoOverlap(start, resumesOn, exceptId: null);
        var period = OutgoingStatusPeriod.Create(Id, reason, start, resumesOn, comment);
        _statusPeriods.Add(period);
        return period;
    }

    private OutgoingAmountChange FindAmountChange(Guid id) =>
        _amountChanges.SingleOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("That amount change doesn't exist.");

    private OutgoingStatusPeriod FindStatusPeriod(Guid id) =>
        _statusPeriods.SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("That pause doesn't exist.");

    private void EnsureNoAmountChangeOn(DateOnly date, Guid? exceptId)
    {
        if (_amountChanges.Any(c => c.EffectiveFrom == date && c.Id != exceptId))
        {
            throw new InvalidOperationException("There's already an amount change on that date. Change that one instead.");
        }
    }

    private void EnsurePausedOnPaymentDates(DateOnly from, DateOnly? resumesOn)
    {
        if (!IsPaymentDate(from))
        {
            throw new InvalidOperationException("A pause has to start on a payment date.");
        }

        if (resumesOn is { } resumes && resumes > from && !IsPaymentDate(resumes))
        {
            throw new InvalidOperationException("A pause has to resume on a payment date.");
        }
    }

    private void EnsureNoOverlap(DateOnly start, DateOnly? resumesOn, Guid? exceptId)
    {
        if (_statusPeriods.Any(p => p.Id != exceptId && p.Overlaps(start, resumesOn)))
        {
            throw new InvalidOperationException("That overlaps another pause or the end of this outgoing.");
        }
    }

    private static string? ValidWebsite(string? website)
    {
        var text = Values.Optional(website, nameof(website), WebsiteMaxLength);
        if (text is null)
        {
            return null;
        }

        // Allow "netflix.com" as well as "https://netflix.com".
        var withScheme = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !uri.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("Enter a website address, e.g. https://example.com.", nameof(website));
        }

        return uri.ToString();
    }
}
