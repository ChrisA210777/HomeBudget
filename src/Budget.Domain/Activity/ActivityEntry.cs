using Budget.Domain.Households;

namespace Budget.Domain.Activity;

/// <summary>What part of the household's data a change was to (BUD-139), for links and filters.</summary>
public enum ActivityArea
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
/// One change to a household's data, for its members to see what's been done (BUD-139): who, when, the item and a
/// sentence written at the time, e.g. "paused Netflix from 1 Dec" (shown after the person's name). For visibility,
/// not audit: entries are kept for three months, can't be edited, and aren't exported.
/// </summary>
public sealed class ActivityEntry : IHouseholdOwned
{
    public const int SummaryMaxLength = 300;
    public const int ActorNameMaxLength = 200;
    public const int MergeKeyMaxLength = 100;

    /// <summary>How long entries are kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(91);

    /// <summary>Repeated edits to the same item by the same person within this time become one entry.</summary>
    public static readonly TimeSpan MergeWithin = TimeSpan.FromMinutes(10);

    private ActivityEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Who made the change, or <c>null</c> for the system.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Their name at the time, e.g. "Chris".</summary>
    public string ActorName { get; private set; } = string.Empty;

    public ActivityArea Area { get; private set; }

    /// <summary>The item changed, e.g. the outgoing, while it exists; <c>null</c> for the household itself.</summary>
    public Guid? SubjectId { get; private set; }

    /// <summary>What was done, after the person's name: "paused Netflix from 1 Dec".</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>Set for edits that can be merged with the same person's edit of the same item just before.</summary>
    public string? MergeKey { get; private set; }

    public static ActivityEntry Create(Guid householdId, ActivityArea area, Guid? subjectId, string summary, string? mergeKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return new ActivityEntry
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Area = area,
            SubjectId = subjectId,
            Summary = summary.Length > SummaryMaxLength ? summary[..(SummaryMaxLength - 1)] + "…" : summary,
            MergeKey = mergeKey,
        };
    }

    /// <summary>Who made the change and when; filled in as it's saved.</summary>
    public void Stamp(DateTimeOffset at, Guid? userId, string actorName)
    {
        At = at;
        UserId = userId;
        ActorName = actorName.Length > ActorNameMaxLength ? actorName[..ActorNameMaxLength] : actorName;
    }

    /// <summary>Whether <paramref name="newer"/> is the same person editing the same item again soon after.</summary>
    public bool CanMerge(ActivityEntry newer)
    {
        ArgumentNullException.ThrowIfNull(newer);
        return MergeKey is not null && MergeKey == newer.MergeKey && UserId == newer.UserId && HouseholdId == newer.HouseholdId
            && newer.At - At <= MergeWithin && newer.At >= At;
    }

    /// <summary>Takes on a later edit's time and wording.</summary>
    public void Merge(ActivityEntry newer)
    {
        ArgumentNullException.ThrowIfNull(newer);
        At = newer.At;
        Summary = newer.Summary;
    }
}
