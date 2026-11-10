using Microsoft.AspNetCore.Identity;

namespace Budget.Infrastructure.Identity;

/// <summary>
/// A person who can sign in. Users sign in only with external providers (see ADR-002),
/// so no password hash is ever stored. Keys are GUIDs.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 200;

    /// <summary>The name shown in the app, taken from the external provider at sign-up.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The household shown after sign-in (see ADR-008).</summary>
    public Guid? LastActiveHouseholdId { get; set; }

    // ---- Settings (BUD-87): the defaults are 0, so existing users get them without a data migration. ----

    /// <summary>The first day of the week in week views, e.g. upcoming payments (BUD-82).</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>Whether Outgoings shows a month or 12 months (BUD-80).</summary>
    public ListPeriod OutgoingsPeriod { get; set; } = ListPeriod.Month;

    /// <summary>How Outgoings is grouped (BUD-80).</summary>
    public OutgoingsGrouping OutgoingsGrouping { get; set; } = OutgoingsGrouping.Category;

    /// <summary>Whether Income shows a month or 12 months (BUD-80).</summary>
    public ListPeriod IncomePeriod { get; set; } = ListPeriod.Month;
}

/// <summary>A month or 12 months.</summary>
public enum ListPeriod
{
    Month = 0,
    Year = 1,
}

/// <summary>How the Outgoings list is grouped.</summary>
public enum OutgoingsGrouping
{
    Category = 0,
    PaidFrom = 1,
}
