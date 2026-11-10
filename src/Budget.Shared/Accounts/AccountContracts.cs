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
    int OutgoingCount);

/// <summary>Adds an account, or a pot in <see cref="ParentAccountId"/>; or changes one (the parent can't change).</summary>
public sealed record SaveAccountRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Provider { get; init; }

    /// <summary>A person in the household, or <c>null</c> for the household's own account. Ignored for a pot.</summary>
    public Guid? OwnerPersonId { get; init; }

    public Guid? ParentAccountId { get; init; }
}

public sealed record MergeAccountRequest
{
    /// <summary>The account or pot that everything moves to.</summary>
    public Guid IntoAccountId { get; init; }
}
