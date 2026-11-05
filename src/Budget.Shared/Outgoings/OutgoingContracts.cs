using System.Text.Json.Serialization;

namespace Budget.Shared.Outgoings;

// These enums mirror the domain enums by name (ADR-011) and travel as strings.

[JsonConverter(typeof(JsonStringEnumConverter<OutgoingKindDto>))]
public enum OutgoingKindDto
{
    Bill,
    Savings,
    Allowance,
}

[JsonConverter(typeof(JsonStringEnumConverter<OutgoingPriorityDto>))]
public enum OutgoingPriorityDto
{
    Essential,
    Preferred,
    Optional,
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentUnitDto>))]
public enum PaymentUnitDto
{
    Week,
    Month,
    Year,
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethodDto>))]
public enum PaymentMethodDto
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

[JsonConverter(typeof(JsonStringEnumConverter<AlertPeriodDto>))]
public enum AlertPeriodDto
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

public static class OutgoingRoutes
{
    /// <summary>POST: add an outgoing.</summary>
    public static string Outgoings(Guid householdId) => $"/api/households/{householdId}/outgoings";

    /// <summary>GET: the outgoings for a month. <paramref name="all"/> includes ones not started or already ended.</summary>
    public static string ForMonth(Guid householdId, int year, int month, bool all = false) =>
        $"{Outgoings(householdId)}?month={year:D4}-{month:D2}{(all ? "&all=true" : string.Empty)}";

    public static string Outgoing(Guid householdId, Guid outgoingId) => $"{Outgoings(householdId)}/{outgoingId}";

    /// <summary>GET: the "paid from" account names already used in the household, for suggestions.</summary>
    public static string PaidFromNames(Guid householdId) => $"{Outgoings(householdId)}/paid-from";
}

public sealed record CreateOutgoingRequest
{
    public OutgoingKindDto Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    public OutgoingPriorityDto? Priority { get; init; }

    /// <summary>Who it's for; <c>null</c> for the whole household.</summary>
    public Guid? PersonId { get; init; }

    public decimal? Amount { get; init; }

    public int Every { get; init; } = 1;

    public PaymentUnitDto Unit { get; init; } = PaymentUnitDto.Month;

    public DateOnly? FirstPaymentDate { get; init; }

    /// <summary>Optional; defaults to "Starting amount".</summary>
    public string? Comment { get; init; }

    public string? Recipient { get; init; }

    public PaymentMethodDto? PaymentMethod { get; init; }

    public string? PaidFrom { get; init; }

    public string? ReferenceLabel { get; init; }

    public string? ReferenceValue { get; init; }

    public string? Website { get; init; }

    public string? ContactPhone { get; init; }

    public string? Notes { get; init; }

    /// <summary>The next renewal or anniversary; <c>null</c> if it doesn't renew.</summary>
    public DateOnly? RenewalDate { get; init; }

    public int RenewalEveryMonths { get; init; } = 12;

    public bool AutoRenews { get; init; }

    public AlertPeriodDto AlertPeriod { get; init; }
}

/// <summary>What's returned once an outgoing has been added.</summary>
public sealed record OutgoingCreatedResponse(Guid Id, string Name, decimal MonthlyAmount);

/// <summary>
/// The household's outgoings for one month (BUD-50). Totals aren't included because the page
/// filters the items and works the totals out with <see cref="OutgoingTotals"/>.
/// </summary>
public sealed record OutgoingMonthResponse(int Year, int Month, IReadOnlyList<OutgoingMonthItem> Items, string CurrencyCode);

/// <summary>
/// One outgoing in a month.
/// <para><see cref="MonthlyAmount"/> is rounded to pennies for showing; <see cref="ExactMonthlyAmount"/>
/// isn't, so totals can be rounded once.</para>
/// <para><see cref="Amount"/>, <see cref="Every"/> and <see cref="Unit"/> are what's actually paid, as at the end of the month
/// (or the first amount, if it hasn't started).</para>
/// <para><see cref="Notes"/> explain why the month differs, e.g. "Paused 1 Nov – 14 Nov".</para>
/// </summary>
public sealed record OutgoingMonthItem(
    Guid Id,
    string Name,
    OutgoingKindDto Kind,
    OutgoingPriorityDto Priority,
    Guid CategoryId,
    string CategoryName,
    int CategorySortOrder,
    Guid? PersonId,
    string? PersonName,
    decimal Amount,
    int Every,
    PaymentUnitDto Unit,
    decimal MonthlyAmount,
    decimal ExactMonthlyAmount,
    IReadOnlyList<string> Notes,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    DateOnly? NextRenewal,
    bool IsComingUp);

/// <summary>Totals for a set of outgoings, each rounded once from the exact amounts.</summary>
public sealed record OutgoingTotals(decimal Total, decimal Essential, decimal Preferred, decimal Optional)
{
    public static OutgoingTotals For(IEnumerable<OutgoingMonthItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        decimal Sum(Func<OutgoingMonthItem, bool> include) =>
            Math.Round(list.Where(include).Sum(i => i.ExactMonthlyAmount), 2, MidpointRounding.AwayFromZero);

        return new OutgoingTotals(
            Sum(_ => true),
            Sum(i => i.Priority == OutgoingPriorityDto.Essential),
            Sum(i => i.Priority == OutgoingPriorityDto.Preferred),
            Sum(i => i.Priority == OutgoingPriorityDto.Optional));
    }

    /// <summary>The subtotal for some outgoings, e.g. one category, rounded once.</summary>
    public static decimal Subtotal(IEnumerable<OutgoingMonthItem> items) =>
        Math.Round(items.Sum(i => i.ExactMonthlyAmount), 2, MidpointRounding.AwayFromZero);
}
