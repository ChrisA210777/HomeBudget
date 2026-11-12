namespace Budget.Shared.Account;

/// <summary>
/// What deleting your account would do (BUD-151): households only you are in are deleted with it, and households you
/// own that have other members must be handed on (or deleted) first.
/// </summary>
/// <param name="HouseholdsToDelete">Households you own that have no other members. They're deleted with your account.</param>
/// <param name="HouseholdsToHandOn">Households you own that have other members. Make another Admin the owner first, or delete them.</param>
/// <param name="HouseholdsToLeave">How many other households you'll leave. Their budgets stay.</param>
public sealed record AccountDeletionCheck(
    IReadOnlyList<OwnedHousehold> HouseholdsToDelete,
    IReadOnlyList<OwnedHousehold> HouseholdsToHandOn,
    int HouseholdsToLeave)
{
    public bool CanDelete => HouseholdsToHandOn.Count == 0;
}

/// <param name="OtherMembers">How many members it has besides you.</param>
public sealed record OwnedHousehold(Guid HouseholdId, string Name, int OtherMembers);

/// <summary>Delete your account. <see cref="Confirmation"/> must be <see cref="Expected"/>, typed by the person.</summary>
public sealed record DeleteAccountRequest
{
    public const string Expected = "DELETE";

    public string? Confirmation { get; init; }

    /// <summary>What they typed is DELETE, ignoring case and spaces at either end.</summary>
    public static bool IsConfirmed(string? typed) => string.Equals(typed?.Trim(), Expected, StringComparison.OrdinalIgnoreCase);
}
