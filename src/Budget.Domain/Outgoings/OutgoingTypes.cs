namespace Budget.Domain.Outgoings;

/// <summary>What sort of outgoing it is (ADR-011).</summary>
public enum OutgoingKind
{
    /// <summary>Paid to an outside company or person, e.g. council tax.</summary>
    Bill,

    /// <summary>Set aside into the household's own pot or account, e.g. holiday savings.</summary>
    Savings,

    /// <summary>A spending budget rather than a bill, e.g. food.</summary>
    Allowance,
}

/// <summary>How important it is to pay.</summary>
public enum OutgoingPriority
{
    /// <summary>Has to be paid, e.g. mortgage or council tax.</summary>
    Essential,

    /// <summary>Should ideally be paid, e.g. boiler service or pet insurance.</summary>
    Preferred,

    /// <summary>A choice, e.g. streaming or a gym.</summary>
    Optional,
}

public enum PaymentUnit
{
    Week,
    Month,
    Year,
}

public enum PaymentMethod
{
    DirectDebit,
    StandingOrder,
    Cash,
    CreditCard,
    DebitCard,
    Cheque,
    BankTransfer,
    Other,
}

/// <summary>How long before a renewal the outgoing is highlighted as coming up.</summary>
public enum AlertPeriod
{
    None,
    OneDay,
    TwoDays,
    ThreeDays,
    OneWeek,
    TwoWeeks,
    OneMonth,
    TwoMonths,
    ThreeMonths,
    SixMonths,
}

public enum OutgoingStatusReason
{
    /// <summary>Not paid for a while, e.g. a payment holiday; may have a resumption date.</summary>
    Paused,

    /// <summary>Stopped for good from a date.</summary>
    Ended,
}

/// <summary>An amount is paid every <see cref="Every"/> <see cref="Unit"/>s, e.g. every 1 Month.</summary>
public readonly record struct PaymentSchedule(int Every, PaymentUnit Unit)
{
    public const int MaxEvery = 99;

    public static PaymentSchedule Weekly => new(1, PaymentUnit.Week);

    public static PaymentSchedule Monthly => new(1, PaymentUnit.Month);

    public static PaymentSchedule Yearly => new(1, PaymentUnit.Year);

    public PaymentSchedule Validated()
    {
        if (Every is < 1 or > MaxEvery)
        {
            throw new ArgumentOutOfRangeException(nameof(Every), Every, $"How often must be between 1 and {MaxEvery}.");
        }

        if (!Enum.IsDefined(Unit))
        {
            throw new ArgumentOutOfRangeException(nameof(Unit), Unit, "Unknown unit.");
        }

        return this;
    }

    /// <summary>
    /// The amount averaged over a month (ADR-011): weeks × 52 ÷ 12, years ÷ 12, divided by how many
    /// units apart the payments are. Unrounded.
    /// </summary>
    public decimal MonthlyEquivalent(decimal amount) => Unit switch
    {
        PaymentUnit.Week => amount * 52m / 12m / Every,
        PaymentUnit.Month => amount / Every,
        PaymentUnit.Year => amount / (12m * Every),
        _ => throw new InvalidOperationException("Unknown unit."),
    };

    /// <summary>The first payment on or after <paramref name="date"/>, counting from <paramref name="first"/>.</summary>
    public DateOnly NextOnOrAfter(DateOnly first, DateOnly date)
    {
        if (date <= first)
        {
            return first;
        }

        switch (Unit)
        {
            case PaymentUnit.Week:
                var periodDays = 7 * Every;
                var periods = (date.DayNumber - first.DayNumber + periodDays - 1) / periodDays;
                return first.AddDays(periods * periodDays);

            case PaymentUnit.Month:
            case PaymentUnit.Year:
                var months = Unit == PaymentUnit.Month ? Every : 12 * Every;
                return Dates.NextMonthlyOnOrAfter(first, months, date);

            default:
                throw new InvalidOperationException("Unknown unit.");
        }
    }

    /// <summary>e.g. "a month", "every 2 months", "a year", "every 4 weeks".</summary>
    public string Describe()
    {
        var unit = Unit switch
        {
            PaymentUnit.Week => "week",
            PaymentUnit.Month => "month",
            _ => "year",
        };
        return Every == 1 ? $"a {unit}" : $"every {Every} {unit}s";
    }
}

internal static class Dates
{
    /// <summary>
    /// The first date on or after <paramref name="date"/> in the series <paramref name="anchor"/>,
    /// anchor + n months, anchor + 2n months... Always counted from the anchor, so 31 Jan gives
    /// 28/29 Feb then 31 Mar rather than drifting to the 28th.
    /// </summary>
    public static DateOnly NextMonthlyOnOrAfter(DateOnly anchor, int months, DateOnly date)
    {
        if (date <= anchor)
        {
            return anchor;
        }

        var monthsBetween = ((date.Year - anchor.Year) * 12) + date.Month - anchor.Month;
        var steps = Math.Max(0, monthsBetween / months);
        var candidate = anchor.AddMonths(steps * months);
        while (candidate < date)
        {
            steps++;
            candidate = anchor.AddMonths(steps * months);
        }

        return candidate;
    }

    public static DateOnly Subtract(DateOnly date, AlertPeriod period) => period switch
    {
        AlertPeriod.OneDay => date.AddDays(-1),
        AlertPeriod.TwoDays => date.AddDays(-2),
        AlertPeriod.ThreeDays => date.AddDays(-3),
        AlertPeriod.OneWeek => date.AddDays(-7),
        AlertPeriod.TwoWeeks => date.AddDays(-14),
        AlertPeriod.OneMonth => date.AddMonths(-1),
        AlertPeriod.TwoMonths => date.AddMonths(-2),
        AlertPeriod.ThreeMonths => date.AddMonths(-3),
        AlertPeriod.SixMonths => date.AddMonths(-6),
        _ => date,
    };
}
