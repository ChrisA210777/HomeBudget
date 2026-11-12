namespace Budget.Shared.Accounts;

public static class HouseholdAccountRoutes
{
    /// <summary>GET: the household's accounts and pots. POST: add one (BUD-89).</summary>
    public static string Accounts(Guid householdId) => $"/api/households/{householdId}/accounts";

    /// <summary>PUT: rename, or change the provider or type.</summary>
    public static string Account(Guid householdId, Guid accountId) => $"{Accounts(householdId)}/{accountId}";

    /// <summary>POST: archive, so it isn't offered for new entries.</summary>
    public static string Archive(Guid householdId, Guid accountId) => $"{Account(householdId, accountId)}/archive";

    /// <summary>POST: bring back from the archive.</summary>
    public static string Restore(Guid householdId, Guid accountId) => $"{Account(householdId, accountId)}/restore";

    /// <summary>PUT (Users): when the account's month starts, e.g. from Cash flow's suggestion (BUD-136).</summary>
    public static string MonthStart(Guid householdId, Guid accountId) => $"{Account(householdId, accountId)}/month-start";

    /// <summary>POST: move everything to another account or pot, then archive this one (Admins).</summary>
    public static string Merge(Guid householdId, Guid accountId) => $"{Account(householdId, accountId)}/merge";
}

/// <summary>
/// An account, or a pot when <see cref="ParentAccountId"/> is set (BUD-89, ADR-014). Accounts are listed by name,
/// each followed by its pots. <see cref="DisplayName"/> is "Account" or "Account › Pot". The owner is a person, or
/// <c>null</c> for the household's own (joint) account; a pot belongs to its account's owner.
/// </summary>
public sealed record AccountSummary(
    Guid Id,
    string Name,
    string? Provider,
    Guid? OwnerPersonId,
    string? OwnerName,
    Guid? ParentAccountId,
    bool IsArchived,
    string DisplayName,
    int OutgoingCount,
    int IncomeCount = 0)
{
    /// <summary>
    /// The income or transfer whose arrival starts this account's month (BUD-136), or <c>null</c> for the 1st.
    /// Always <c>null</c> for a pot.
    /// </summary>
    public Guid? MonthStartsWithId { get; init; }

    /// <summary>The chosen payment's name, e.g. "Salary" or "From Chris current".</summary>
    public string? MonthStartsWithName { get; init; }

    /// <summary>
    /// The chosen payment no longer exists, so the month starts on the 1st again; the account page says so.
    /// </summary>
    public bool MonthStartMissing { get; init; }

    /// <summary>The regular money into the account (or its pots) that its month can start with: income and transfers in.</summary>
    public IReadOnlyList<MonthStartOption> MonthStartOptions { get; init; } = [];
}

/// <summary>An income or transfer into an account, which can start its month (BUD-136).</summary>
public sealed record MonthStartOption(Guid Id, string Name);

/// <summary>When an account's month starts: <c>null</c> for the 1st, or one of its <see cref="AccountSummary.MonthStartOptions"/>.</summary>
public sealed record SetMonthStartRequest
{
    public Guid? MonthStartsWithId { get; init; }
}

/// <summary>Adds an account, or a pot in <see cref="ParentAccountId"/>; or changes one (the parent can't change).</summary>
public sealed record SaveAccountRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Provider { get; init; }

    /// <summary>A person in the household, or <c>null</c> for the household's own account. Ignored for a pot.</summary>
    public Guid? OwnerPersonId { get; init; }

    public Guid? ParentAccountId { get; init; }

    /// <summary>
    /// The income or transfer whose arrival starts the account's month, or <c>null</c> for the 1st (BUD-136). Ignored
    /// for a pot, and when adding.
    /// </summary>
    public Guid? MonthStartsWithId { get; init; }
}

public sealed record MergeAccountRequest
{
    /// <summary>The account or pot that everything moves to.</summary>
    public Guid IntoAccountId { get; init; }
}
