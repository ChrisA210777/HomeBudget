using System.Text.Json.Serialization;

namespace Budget.Shared.Account;

/// <summary>Whether a list shows amounts for a month or for 12 months (BUD-80).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ListPeriodDto>))]
public enum ListPeriodDto
{
    Month,
    Year,
}

/// <summary>How the Outgoings list is grouped (BUD-80).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutgoingsGroupingDto>))]
public enum OutgoingsGroupingDto
{
    Category,
    PaidFrom,
}

/// <summary>
/// The signed-in user's preferences (BUD-87, ADR-013). They're stored with the user, not the household,
/// so they follow them to every device and every household.
/// </summary>
public sealed record UserSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek FirstDayOfWeek = DayOfWeek.Sunday,
    ListPeriodDto OutgoingsPeriod = ListPeriodDto.Month,
    OutgoingsGroupingDto OutgoingsGrouping = OutgoingsGroupingDto.Category,
    ListPeriodDto IncomePeriod = ListPeriodDto.Month)
{
    /// <summary>What a new user gets.</summary>
    public static UserSettings Defaults { get; } = new();
}

/// <summary>Changes some settings; anything left out stays as it is.</summary>
public sealed record UpdateUserSettingsRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))]
    public DayOfWeek? FirstDayOfWeek { get; init; }

    public ListPeriodDto? OutgoingsPeriod { get; init; }

    public OutgoingsGroupingDto? OutgoingsGrouping { get; init; }

    public ListPeriodDto? IncomePeriod { get; init; }

    /// <summary>The field names of any values that aren't allowed.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        Check(errors, nameof(FirstDayOfWeek), FirstDayOfWeek, "Choose a day of the week.");
        Check(errors, nameof(OutgoingsPeriod), OutgoingsPeriod, "Choose Month or Year.");
        Check(errors, nameof(OutgoingsGrouping), OutgoingsGrouping, "Choose Category or Paid from.");
        Check(errors, nameof(IncomePeriod), IncomePeriod, "Choose Month or Year.");
        return errors;
    }

    private static void Check<T>(Dictionary<string, string[]> errors, string key, T? value, string message)
        where T : struct, Enum
    {
        if (value is { } v && !Enum.IsDefined(v))
        {
            errors[key] = [message];
        }
    }
}
