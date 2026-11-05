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
