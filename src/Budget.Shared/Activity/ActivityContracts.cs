using Budget.Shared.Households;

namespace Budget.Shared.Activity;

public static class ActivityRoutes
{
    /// <summary>GET (Viewers): recent activity, newest first (BUD-139).</summary>
    /// <param name="take">How many, at most 100.</param>
    /// <param name="days">How far back, at most the three months kept.</param>
    /// <param name="before">For more: the time of the last one already shown.</param>
    /// <param name="who">Only changes made by this member (BUD-140).</param>
    /// <param name="areas">Only changes to these parts of the household's data (BUD-140).</param>
    public static string Activity(
        Guid householdId, int take, int? days = null, DateTimeOffset? before = null, Guid? who = null, IReadOnlyCollection<ActivityAreaDto>? areas = null) =>
        $"{HouseholdRoutes.Household(householdId)}/activity?take={take}"
        + (days is { } d ? $"&days={d}" : string.Empty)
        + (before is { } b ? $"&before={Uri.EscapeDataString(b.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}" : string.Empty)
        + (who is { } w ? $"&who={w}" : string.Empty)
        + (areas is { Count: > 0 } a ? $"&areas={string.Join(',', a)}" : string.Empty);
}

/// <summary>What part of the household's data a change was to.</summary>
public enum ActivityAreaDto
{
    Household,
    Members,
    People,
    Income,
    Outgoings,
    Categories,
    Accounts,
    Transfers,
}

/// <summary>
/// One change, e.g. <c>Who</c> "Chris", <c>Summary</c> "paused Netflix from 1 Dec 2026". Amounts in the summary are
/// written as <c>{{480.00}}</c>, to show in the household's currency.
/// </summary>
/// <param name="Exists">The item it's about still exists, so it can be linked to.</param>
public sealed record ActivityItem(Guid Id, DateTimeOffset At, string Who, string Summary, ActivityAreaDto Area, Guid? SubjectId, bool Exists);

public sealed record ActivityResponse(string CurrencyCode, IReadOnlyList<ActivityItem> Items)
{
    /// <summary>Who has made changes in the time kept, to filter by (BUD-140).</summary>
    public IReadOnlyList<ActivityPerson> People { get; init; } = [];

    /// <summary>There are older changes than these, to show more.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Someone who has made changes, by the name on their latest one.</summary>
public sealed record ActivityPerson(Guid UserId, string Name);
