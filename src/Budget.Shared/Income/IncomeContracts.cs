using System.Text.Json.Serialization;
using Budget.Shared.Outgoings;

namespace Budget.Shared.Income;

/// <summary>Why a leave or pause changes an income. Mirrors the domain enum by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomeStatusReasonDto>))]
public enum IncomeStatusReasonDto
{
    Holiday,
    SickLeave,
    UnpaidLeave,
    ParentalLeave,
    Ended,
    Other,
}

public static class IncomeRoutes
{
    /// <summary>GET: income for a month (<c>?month=2026-11</c>, optional <c>&amp;all=true</c>). POST: add an income.</summary>
    public static string IncomeStreams(Guid householdId) => $"/api/households/{householdId}/income-streams";

    /// <summary>GET: the income for a month, or with <paramref name="months"/> = 12 for the 12 months starting with it (BUD-80).</summary>
    public static string ForMonth(Guid householdId, int year, int month, bool all = false, int months = 1) =>
        $"{IncomeStreams(householdId)}?month={year:D4}-{month:D2}{(all ? "&all=true" : string.Empty)}{(months == 1 ? string.Empty : $"&months={months}")}";

    /// <summary>GET: details and history. PUT: rename or change person. DELETE: delete the income.</summary>
    public static string IncomeStream(Guid householdId, Guid incomeStreamId) => $"{IncomeStreams(householdId)}/{incomeStreamId}";

    /// <summary>POST: change the amount from a date.</summary>
    public static string AmountChanges(Guid householdId, Guid incomeStreamId) => $"{IncomeStream(householdId, incomeStreamId)}/amount-changes";

    /// <summary>PUT: correct an amount change. DELETE (with a reason): remove one recorded by mistake.</summary>
    public static string AmountChange(Guid householdId, Guid incomeStreamId, Guid changeId) => $"{AmountChanges(householdId, incomeStreamId)}/{changeId}";

    /// <summary>POST: add leave, a pause or the end of the income.</summary>
    public static string StatusPeriods(Guid householdId, Guid incomeStreamId) => $"{IncomeStream(householdId, incomeStreamId)}/status-periods";

    /// <summary>PUT: correct a period. DELETE (with a reason): remove one recorded by mistake.</summary>
    public static string StatusPeriod(Guid householdId, Guid incomeStreamId, Guid periodId) => $"{StatusPeriods(householdId, incomeStreamId)}/{periodId}";
}

// Requests. Every change to an existing income sends the Version that was loaded, so a save over
// someone else's change is rejected (409) rather than silently overwriting it.

public sealed record CreateIncomeStreamRequest
{
    public string Name { get; init; } = string.Empty;

    public Guid PersonId { get; init; }

    /// <summary>What's received each time, after tax and other deductions.</summary>
    public decimal? Amount { get; init; }

    public int Every { get; init; } = 1;

    public PaymentUnitDto Unit { get; init; } = PaymentUnitDto.Month;

    /// <summary>The first payment; the income starts from this date.</summary>
    public DateOnly? StartsOn { get; init; }

    /// <summary>Optional; defaults to "Starting amount".</summary>
    public string? Comment { get; init; }
}

public sealed record UpdateIncomeStreamRequest
{
    public string Name { get; init; } = string.Empty;

    public Guid PersonId { get; init; }

    public string? Version { get; init; }
}

public sealed record SaveAmountChangeRequest
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

public sealed record SaveStatusPeriodRequest
{
    public IncomeStatusReasonDto Reason { get; init; } = IncomeStatusReasonDto.UnpaidLeave;

    public DateOnly? StartDate { get; init; }

    /// <summary>Inclusive; blank means until further notice.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Blank means no income during the period.</summary>
    public decimal? ReducedMonthlyAmount { get; init; }

    public string Comment { get; init; } = string.Empty;

    /// <summary>Required when correcting an existing entry: why it's being changed.</summary>
    public string? CorrectionReason { get; init; }

    public string? Version { get; init; }
}

public sealed record DeleteEntryRequest
{
    public string Reason { get; init; } = string.Empty;

    public string? Version { get; init; }
}

// Responses. Money is rounded to pennies.

/// <summary>Household income for one month.</summary>
/// <summary>
/// The income for <see cref="Months"/> months starting with <see cref="Year"/>-<see cref="Month"/>: 1 for a month,
/// or 12 for a year (BUD-80), when each item's amount and the total cover all 12 months.
/// </summary>
public sealed record IncomeMonthResponse(int Year, int Month, IReadOnlyList<IncomeMonthItem> Items, decimal Total, string CurrencyCode, int Months = 1);

/// <summary>
/// One income in a month. <see cref="Notes"/> explain why the month differs from the usual amount,
/// e.g. "Unpaid leave 3 Nov – 17 Nov" or "Changes to £3,300.00 a month from 16 Nov".
/// <para><see cref="UsualAmount"/> is the usual monthly equivalent; <see cref="ReceivedAmount"/>,
/// <see cref="Every"/> and <see cref="Unit"/> are what's actually received, e.g. £102.20 every 4 weeks.</para>
/// </summary>
public sealed record IncomeMonthItem(
    Guid Id,
    string Name,
    Guid PersonId,
    string PersonName,
    decimal AmountForMonth,
    decimal? UsualAmount,
    decimal? ReceivedAmount,
    int Every,
    PaymentUnitDto Unit,
    IReadOnlyList<string> Notes,
    DateOnly StartsOn,
    DateOnly? EndsOn);

/// <summary>An income with its full history, for its own page.</summary>
public sealed record IncomeStreamDetails(
    Guid Id,
    string Name,
    Guid PersonId,
    string PersonName,
    decimal AmountToday,
    decimal ReceivedAmount,
    int Every,
    PaymentUnitDto Unit,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    IReadOnlyList<AmountChangeEntry> AmountChanges,
    IReadOnlyList<StatusPeriodEntry> StatusPeriods,
    IReadOnlyList<CorrectionEntry> Corrections,
    string CurrencyCode,
    string Version);

/// <param name="MonthlyAmount">The monthly equivalent of <paramref name="Amount"/> every <paramref name="Every"/> <paramref name="Unit"/>s, rounded.</param>
public sealed record AmountChangeEntry(
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

public sealed record StatusPeriodEntry(
    Guid Id,
    IncomeStatusReasonDto Reason,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? ReducedMonthlyAmount,
    string Comment,
    string? RecordedBy,
    DateTimeOffset RecordedAt,
    bool IsUpcoming);

/// <param name="EntryType">"AmountChange" or "StatusPeriod".</param>
/// <param name="Action">"Edited" or "Deleted".</param>
public sealed record CorrectionEntry(
    string EntryType,
    string Action,
    string Before,
    string? After,
    string Reason,
    string? CorrectedBy,
    DateTimeOffset CorrectedAt);
