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

[JsonConverter(typeof(JsonStringEnumConverter<OutgoingStatusReasonDto>))]
public enum OutgoingStatusReasonDto
{
    Paused,
    Ended,
}

public static class OutgoingRoutes
{
    /// <summary>POST: add an outgoing.</summary>
    public static string Outgoings(Guid householdId) => $"/api/households/{householdId}/outgoings";

    /// <summary>GET: the outgoings for a month. <paramref name="all"/> includes ones not started or already ended.</summary>
    /// <summary>
    /// GET: the outgoings for a month, or with <paramref name="months"/> = 12 for the 12 months starting
    /// with it (BUD-80, ADR-013).
    /// </summary>
    public static string ForMonth(Guid householdId, int year, int month, bool all = false, int months = 1) =>
        $"{Outgoings(householdId)}?month={year:D4}-{month:D2}{(all ? "&all=true" : string.Empty)}{(months == 1 ? string.Empty : $"&months={months}")}";

    /// <summary>GET: details and history. PUT: edit details. DELETE: delete the outgoing.</summary>
    public static string Outgoing(Guid householdId, Guid outgoingId) => $"{Outgoings(householdId)}/{outgoingId}";

    /// <summary>POST: change the amount (and how often) from a date.</summary>
    public static string AmountChanges(Guid householdId, Guid outgoingId) => $"{Outgoing(householdId, outgoingId)}/amount-changes";

    /// <summary>PUT: correct an amount change. DELETE (with a reason): remove one recorded by mistake.</summary>
    public static string AmountChange(Guid householdId, Guid outgoingId, Guid changeId) => $"{AmountChanges(householdId, outgoingId)}/{changeId}";

    /// <summary>POST: pause or end the outgoing.</summary>
    public static string StatusPeriods(Guid householdId, Guid outgoingId) => $"{Outgoing(householdId, outgoingId)}/status-periods";

    /// <summary>PUT: correct a pause or ending. DELETE (with a reason): remove one recorded by mistake.</summary>
    public static string StatusPeriod(Guid householdId, Guid outgoingId, Guid periodId) => $"{StatusPeriods(householdId, outgoingId)}/{periodId}";

    /// <summary>GET: outgoings coming up for renewal now, soonest first (BUD-52).</summary>
    public static string ComingUp(Guid householdId) => $"{Outgoings(householdId)}/coming-up";

    /// <summary>GET: the reference names (e.g. "Policy number") already used in the household, most used first.</summary>
    public static string ReferenceLabels(Guid householdId) => $"{Outgoings(householdId)}/reference-labels";

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

/// <summary>
/// Edits everything except the amounts, schedule and first payment (those are the amount history).
/// Sends the Version that was loaded, so saving over someone else's change is refused (409).
/// </summary>
public sealed record UpdateOutgoingRequest
{
    public OutgoingKindDto Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    public OutgoingPriorityDto? Priority { get; init; }

    public Guid? PersonId { get; init; }

    public string? Recipient { get; init; }

    public PaymentMethodDto? PaymentMethod { get; init; }

    public string? PaidFrom { get; init; }

    public string? ReferenceLabel { get; init; }

    public string? ReferenceValue { get; init; }

    public string? Website { get; init; }

    public string? ContactPhone { get; init; }

    public string? Notes { get; init; }

    public DateOnly? RenewalDate { get; init; }

    public int RenewalEveryMonths { get; init; } = 12;

    public bool AutoRenews { get; init; }

    public AlertPeriodDto AlertPeriod { get; init; }

    public string? Version { get; init; }
}

public sealed record SaveOutgoingAmountRequest
{
    public decimal? Amount { get; init; }

    public int Every { get; init; } = 1;

    public PaymentUnitDto Unit { get; init; } = PaymentUnitDto.Month;

    public DateOnly? EffectiveFrom { get; init; }

    public string Comment { get; init; } = string.Empty;

    /// <summary>Required when correcting an existing entry: why it's being changed.</summary>
    public string? CorrectionReason { get; init; }

    public string? Version { get; init; }
}

/// <summary>
/// A pause skips whole payments: it starts on a payment date and skips <see cref="SkipPayments"/>
/// payments, or carries on until further notice when that's blank. An ending can start on any date.
/// </summary>
public sealed record SaveOutgoingStatusRequest
{
    public OutgoingStatusReasonDto Reason { get; init; } = OutgoingStatusReasonDto.Paused;

    public DateOnly? StartDate { get; init; }

    public int? SkipPayments { get; init; }

    public string Comment { get; init; } = string.Empty;

    /// <summary>Required when correcting an existing entry: why it's being changed.</summary>
    public string? CorrectionReason { get; init; }

    public string? Version { get; init; }
}

public sealed record DeleteOutgoingEntryRequest
{
    public string Reason { get; init; } = string.Empty;

    public string? Version { get; init; }
}

/// <summary>An outgoing with its full history, for its own page (BUD-51).</summary>
/// <param name="Amount">What's paid today (or the first amount, if it hasn't started).</param>
/// <param name="MonthlyAmount">The monthly equivalent of <paramref name="Amount"/>, rounded.</param>
/// <param name="Status">e.g. "Paused until 1 Mar 2027", "Paused until further notice" or "Ended from 1 Jun 2026"; null when active.</param>
/// <param name="UpcomingPayments">The next payment dates (ignoring pauses), for choosing where a pause starts.</param>
public sealed record OutgoingDetailsResponse(
    Guid Id,
    string Name,
    OutgoingKindDto Kind,
    OutgoingPriorityDto Priority,
    Guid CategoryId,
    string CategoryName,
    Guid? PersonId,
    string? PersonName,
    decimal Amount,
    int Every,
    PaymentUnitDto Unit,
    decimal MonthlyAmount,
    DateOnly FirstPaymentDate,
    DateOnly? EndsOn,
    DateOnly? NextPayment,
    DateOnly? RenewalDate,
    int RenewalEveryMonths,
    bool AutoRenews,
    AlertPeriodDto AlertPeriod,
    DateOnly? NextRenewal,
    bool IsComingUp,
    string? Status,
    string? Recipient,
    PaymentMethodDto? PaymentMethod,
    string? PaidFrom,
    string? ReferenceLabel,
    string? ReferenceValue,
    string? Website,
    string? ContactPhone,
    string? Notes,
    IReadOnlyList<DateOnly> UpcomingPayments,
    IReadOnlyList<OutgoingAmountEntry> AmountChanges,
    IReadOnlyList<OutgoingStatusEntry> StatusPeriods,
    IReadOnlyList<OutgoingCorrectionEntry> Corrections,
    string CurrencyCode,
    string Version);

public sealed record OutgoingAmountEntry(
    Guid Id,
    DateOnly EffectiveFrom,
    decimal Amount,
    int Every,
    PaymentUnitDto Unit,
    decimal MonthlyAmount,
    string Comment,
    string? RecordedBy,
    DateTimeOffset RecordedAt,
    bool IsUpcoming,
    bool IsFirst);

/// <param name="SkippedPayments">How many payments a pause skips; null until further notice, or for an ending.</param>
public sealed record OutgoingStatusEntry(
    Guid Id,
    OutgoingStatusReasonDto Reason,
    DateOnly StartDate,
    DateOnly? ResumesOn,
    int? SkippedPayments,
    string Comment,
    string? RecordedBy,
    DateTimeOffset RecordedAt,
    bool IsUpcoming);

/// <param name="EntryType">"AmountChange" or "StatusPeriod".</param>
/// <param name="Action">"Edited" or "Deleted".</param>
public sealed record OutgoingCorrectionEntry(
    string EntryType,
    string Action,
    string Before,
    string? After,
    string Reason,
    string? CorrectedBy,
    DateTimeOffset CorrectedAt);

/// <summary>What's returned once an outgoing has been added.</summary>
public sealed record OutgoingCreatedResponse(Guid Id, string Name, decimal MonthlyAmount);

/// <summary>
/// The household's outgoings for one month (BUD-50). Totals aren't included because the page
/// filters the items and works the totals out with <see cref="OutgoingTotals"/>.
/// </summary>
/// <summary>
/// The outgoings for <see cref="Months"/> months starting with <see cref="Year"/>-<see cref="Month"/>: 1 for a
/// month, or 12 for a year (BUD-80). Each item's amounts are then the total budget for those months.
/// </summary>
public sealed record OutgoingMonthResponse(int Year, int Month, IReadOnlyList<OutgoingMonthItem> Items, string CurrencyCode, int Months = 1);

/// <summary>
/// One outgoing in a month.
/// <para><see cref="MonthlyAmount"/> is rounded to pennies for showing; <see cref="ExactMonthlyAmount"/>
/// isn't, so totals can be rounded once.</para>
/// <para><see cref="Amount"/>, <see cref="Every"/> and <see cref="Unit"/> are what's actually paid, as at the end of the month
/// (or the first amount, if it hasn't started).</para>
/// <para><see cref="Notes"/> explain why the month differs, e.g. "Paused 1 Nov – 14 Nov".</para>
/// <para>For a year (<see cref="OutgoingMonthResponse.Months"/> = 12), <see cref="MonthlyAmount"/> and
/// <see cref="ExactMonthlyAmount"/> are the sum of the 12 monthly budget amounts (ADR-013), and the
/// paid amount is as at the end of the year.</para>
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
    bool IsComingUp,
    bool AutoRenews = false,
    string CategoryIcon = CategoryIcons.Default,
    string? PaidFrom = null);

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

/// <summary>An outgoing in its reminder period before renewing (BUD-52).</summary>
/// <param name="DaysUntil">Days from today until <paramref name="RenewalDate"/>; 0 means today.</param>
public sealed record RenewalComingUp(
    Guid Id,
    string Name,
    DateOnly RenewalDate,
    int DaysUntil,
    bool AutoRenews,
    decimal Amount,
    int Every,
    PaymentUnitDto Unit,
    string CurrencyCode);
