namespace Budget.Domain.Outgoings;

/// <summary>An amount paid on a schedule from a date: an outgoing's or a transfer's amount change.</summary>
public interface IScheduledAmount
{
    DateOnly EffectiveFrom { get; }

    decimal Amount { get; }

    PaymentSchedule Schedule { get; }

    decimal MonthlyEquivalent { get; }
}

/// <summary>A pause, or the end, of an outgoing or transfer.</summary>
public interface IScheduledPause
{
    OutgoingStatusReason Reason { get; }

    DateOnly StartDate { get; }

    DateOnly? ResumesOn { get; }

    bool Covers(DateOnly date);
}

/// <summary>
/// The payment-date and amount rules shared by outgoings and transfers (ADR-011, ADR-013, ADR-014): payments
/// anchored on the first payment date, on the schedule and at the amount in force on each date, skipping
/// pauses and stopping at the end.
/// </summary>
public readonly struct PaymentTimeline(DateOnly firstPaymentDate, IReadOnlyCollection<IScheduledAmount> amounts, IReadOnlyCollection<IScheduledPause> pauses)
{
    public const int MaxPausedPayments = 520;

    /// <summary>When the first amount applies from.</summary>
    public DateOnly StartsOn => amounts.Min(c => c.EffectiveFrom);

    /// <summary>When it ended, if it has.</summary>
    public DateOnly? EndsOn => pauses.Where(p => p.Reason == OutgoingStatusReason.Ended).Select(p => (DateOnly?)p.StartDate).Min();

    /// <summary>Whether it's being paid on a date: started, and not paused or ended.</summary>
    public bool IsActiveOn(DateOnly date) => amounts.Any(c => c.EffectiveFrom <= date) && !pauses.Any(p => p.Covers(date));

    /// <summary>The amount in force on a date (ignoring pauses), if it has started.</summary>
    public IScheduledAmount? AmountOn(DateOnly date) => amounts.Where(c => c.EffectiveFrom <= date).MaxBy(c => c.EffectiveFrom);

    /// <summary>The monthly equivalent on a date: 0 before it starts and while paused or ended.</summary>
    public decimal MonthlyEquivalentOn(DateOnly date) => pauses.Any(p => p.Covers(date)) ? 0m : AmountOn(date)?.MonthlyEquivalent ?? 0m;

    /// <summary>The monthly equivalent for a calendar month (ADR-011). Unrounded. See <see cref="CountingDaysIn"/>.</summary>
    public decimal MonthlyEquivalentForMonth(int year, int month)
    {
        var startsOn = StartsOn;
        var days = CountingDaysIn(year, month).Where(d => d >= startsOn).ToList();
        var timeline = this;
        return days.Count == 0 ? 0m : days.Sum(d => timeline.MonthlyEquivalentOn(d)) / days.Count;
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
        var schedule = ScheduleOn(last);

        if (schedule.Unit != PaymentUnit.Week)
        {
            return [new DateOnly(year, month, Math.Min(firstPaymentDate.Day, DateTime.DaysInMonth(year, month)))];
        }

        var period = 7 * schedule.Every;
        var dates = new List<DateOnly>();
        var next = schedule.NextOnOrAfter(firstPaymentDate, first);
        for (var date = next; date <= last; date = date.AddDays(period))
        {
            dates.Add(date);
        }

        if (dates.Count == 0 && next > firstPaymentDate)
        {
            dates.Add(next.AddDays(-period));
        }

        return dates;
    }

    /// <summary>Whether a payment is due on a date under the schedule in force then (ignoring pauses).</summary>
    public bool IsPaymentDate(DateOnly date) =>
        date >= StartsOn && AmountOn(date)!.Schedule.NextOnOrAfter(firstPaymentDate, date) == date;

    /// <summary>The payment after <paramref name="date"/>, ignoring pauses.</summary>
    public DateOnly PaymentAfter(DateOnly date)
    {
        var from = date < StartsOn ? StartsOn : date.AddDays(1);
        return ScheduleOn(from).NextOnOrAfter(firstPaymentDate, from);
    }

    /// <summary>The next payment on or after a date, or <c>null</c> if none is due (ended, or paused until further notice).</summary>
    public DateOnly? NextPaymentOnOrAfter(DateOnly date)
    {
        var from = date < StartsOn ? StartsOn : date;
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var next = ScheduleOn(from).NextOnOrAfter(firstPaymentDate, from);
            var blocking = pauses.FirstOrDefault(p => p.Covers(next));
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

    /// <summary>
    /// The payments actually due from <paramref name="from"/> to <paramref name="to"/> inclusive (ADR-013):
    /// on the schedule in force on each date, at the amount in force that day. Paused payments are skipped and
    /// there are none before it starts or once it has ended.
    /// </summary>
    public IReadOnlyList<ScheduledPayment> PaymentsBetween(DateOnly from, DateOnly to)
    {
        var payments = new List<ScheduledPayment>();
        for (var next = NextPaymentOnOrAfter(from); next is { } date && date <= to; next = NextPaymentOnOrAfter(date.AddDays(1)))
        {
            var change = AmountOn(date)!;
            payments.Add(new ScheduledPayment(date, change.Amount, change.Schedule));
        }

        return payments;
    }

    public bool IsPausedOn(DateOnly date) => pauses.Any(p => p.Reason == OutgoingStatusReason.Paused && p.Covers(date));

    /// <summary>When payments resume after skipping <paramref name="payments"/> payments from <paramref name="firstSkipped"/>.</summary>
    public DateOnly ResumesAfterSkipping(DateOnly firstSkipped, int payments)
    {
        if (payments is < 1 or > MaxPausedPayments)
        {
            throw new ArgumentOutOfRangeException(nameof(payments), $"A pause skips from 1 to {MaxPausedPayments} payments.");
        }

        var resumesOn = firstSkipped;
        for (var i = 0; i < payments; i++)
        {
            resumesOn = PaymentAfter(resumesOn);
        }

        return resumesOn;
    }

    /// <summary>How many payments a pause skips, or <c>null</c> if it's until further notice (or an ending).</summary>
    public int? PaymentsSkippedBy(IScheduledPause period)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period.Reason != OutgoingStatusReason.Paused || period.ResumesOn is not { } resumes)
        {
            return null;
        }

        var count = 0;
        for (var date = period.StartDate; date < resumes && count <= MaxPausedPayments; date = PaymentAfter(date))
        {
            count++;
        }

        return count;
    }

    /// <summary>The next <paramref name="count"/> payment dates on or after <paramref name="from"/>, ignoring pauses.</summary>
    public IReadOnlyList<DateOnly> PaymentDatesFrom(DateOnly from, int count)
    {
        var dates = new List<DateOnly>(count);
        var start = from < StartsOn ? StartsOn : from;
        var date = ScheduleOn(start).NextOnOrAfter(firstPaymentDate, start);
        for (var i = 0; i < count; i++)
        {
            dates.Add(date);
            date = PaymentAfter(date);
        }

        return dates;
    }

    /// <summary>Checks a pause starts, and resumes, on payment dates.</summary>
    public void EnsurePausedOnPaymentDates(DateOnly from, DateOnly? resumesOn)
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

    private PaymentSchedule ScheduleOn(DateOnly date) => (AmountOn(date) ?? amounts.MinBy(c => c.EffectiveFrom)!).Schedule;
}
