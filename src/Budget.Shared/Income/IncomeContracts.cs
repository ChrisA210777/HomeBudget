using System.Text.Json.Serialization;

namespace Budget.Shared.Income;

// These mirror the domain enums by name; they're sent as strings in JSON.

[JsonConverter(typeof(JsonStringEnumConverter<IncomeStreamTypeDto>))]
public enum IncomeStreamTypeDto
{
    Employment,
}

[JsonConverter(typeof(JsonStringEnumConverter<PayFrequencyDto>))]
public enum PayFrequencyDto
{
    Weekly,
    FourWeekly,
    Monthly,
}

[JsonConverter(typeof(JsonStringEnumConverter<IncomeKindDto>))]
public enum IncomeKindDto
{
    Fixed,
    Variable,
}

[JsonConverter(typeof(JsonStringEnumConverter<AmountBasisDto>))]
public enum AmountBasisDto
{
    Annual,
    Monthly,
    FourWeekly,
    Weekly,
}

public static class IncomeRoutes
{
    /// <summary>GET: list (add <c>?includeInactive=true</c> for ended streams). POST: add employment income.</summary>
    public static string IncomeStreams(Guid householdId) => $"/api/households/{householdId}/income-streams";

    /// <summary>GET: details for editing. PUT: update. DELETE: delete permanently.</summary>
    public static string IncomeStream(Guid householdId, Guid incomeStreamId) =>
        $"{IncomeStreams(householdId)}/{incomeStreamId}";

    /// <summary>POST: stop counting the stream in totals (e.g. the job ended).</summary>
    public static string Deactivate(Guid householdId, Guid incomeStreamId) => $"{IncomeStream(householdId, incomeStreamId)}/deactivate";

    /// <summary>POST: count the stream in totals again.</summary>
    public static string Reactivate(Guid householdId, Guid incomeStreamId) => $"{IncomeStream(householdId, incomeStreamId)}/reactivate";
}

/// <summary>
/// Add or edit employment income. For fixed pay set <see cref="GrossAmount"/> and <see cref="AmountBasis"/>;
/// for variable pay set the lowest, average and highest gross per pay period.
/// </summary>
public sealed record SaveEmploymentIncomeRequest
{
    public string Name { get; init; } = string.Empty;

    public string? EmployerName { get; init; }

    /// <summary>The household member this income belongs to, if any.</summary>
    public Guid? MemberUserId { get; init; }

    public PayFrequencyDto PayFrequency { get; init; } = PayFrequencyDto.Monthly;

    public IncomeKindDto Kind { get; init; } = IncomeKindDto.Fixed;

    public decimal? GrossAmount { get; init; }

    public AmountBasisDto? AmountBasis { get; init; }

    public decimal? LowestGross { get; init; }

    public decimal? AverageGross { get; init; }

    public decimal? HighestGross { get; init; }

    /// <summary>
    /// When editing: the <see cref="EmploymentIncomeDetails.Version"/> that was loaded. The save is
    /// rejected if someone else has changed the stream since.
    /// </summary>
    public string? Version { get; init; }
}

/// <summary>An employment income stream with everything needed to edit it.</summary>
public sealed record EmploymentIncomeDetails(
    Guid Id,
    string Name,
    string? EmployerName,
    Guid? MemberUserId,
    PayFrequencyDto PayFrequency,
    IncomeKindDto Kind,
    decimal? GrossAmount,
    AmountBasisDto? AmountBasis,
    decimal? LowestGross,
    decimal? AverageGross,
    decimal? HighestGross,
    bool IsActive,
    string Version);

/// <summary>One income stream in the list. Money is rounded to pennies.</summary>
public sealed record IncomeStreamSummary(
    Guid Id,
    IncomeStreamTypeDto Type,
    string Name,
    string? EmployerName,
    Guid? MemberUserId,
    string? MemberName,
    PayFrequencyDto PayFrequency,
    IncomeKindDto Kind,
    bool IsActive,
    decimal LowPerPay,
    decimal AveragePerPay,
    decimal HighPerPay,
    decimal LowAnnual,
    decimal AverageAnnual,
    decimal HighAnnual);

/// <summary>
/// The household's income streams, with estimated gross totals across active streams.
/// Variable pay counts at its average.
/// </summary>
public sealed record IncomeStreamListResponse(
    IReadOnlyList<IncomeStreamSummary> Items,
    decimal TotalAnnual,
    decimal TotalMonthly,
    string CurrencyCode);
