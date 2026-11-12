namespace Budget.Shared.Account;

/// <summary>
/// What deleting your account would do (BUD-151, ADR-016): households only you are in are deleted with it, households
/// you own that others use pass to someone else, and you leave the rest.
/// </summary>
/// <param name="HouseholdsToDelete">Households you own that have no other members. They're deleted with your account.</param>
/// <param name="HouseholdsToPassOn">Households you own that have other members, and who'll become their owner.</param>
/// <param name="HouseholdsToLeave">How many other households you'll leave. Their budgets stay.</param>
public sealed record AccountDeletionCheck(
    IReadOnlyList<OwnedHousehold> HouseholdsToDelete,
    IReadOnlyList<PassedOnHousehold> HouseholdsToPassOn,
    int HouseholdsToLeave);

public sealed record OwnedHousehold(Guid HouseholdId, string Name);

/// <param name="NewOwner">The name of who'll become its owner: the longest-standing other Admin, or the longest-standing member.</param>
/// <param name="BecomesAdmin">They're not an Admin yet, so they'll be made one.</param>
public sealed record PassedOnHousehold(Guid HouseholdId, string Name, string NewOwner, bool BecomesAdmin);

/// <summary>Delete your account. <see cref="Confirmation"/> must be <see cref="Expected"/>, typed by the person.</summary>
public sealed record DeleteAccountRequest
{
    public const string Expected = "DELETE";

    public string? Confirmation { get; init; }

    /// <summary>What they typed is DELETE, ignoring case and spaces at either end.</summary>
    public static bool IsConfirmed(string? typed) => string.Equals(typed?.Trim(), Expected, StringComparison.OrdinalIgnoreCase);
}
