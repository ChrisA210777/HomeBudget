namespace Budget.Shared.Account;

/// <summary>An external sign-in provider, e.g. Microsoft.</summary>
/// <param name="Name">The scheme name used in <see cref="AccountRoutes.Login"/>.</param>
/// <param name="DisplayName">The name to show on the sign-in button.</param>
public sealed record AuthProvider(string Name, string DisplayName);

/// <summary>The signed-in user and the households they belong to.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string DisplayName,
    string? Email,
    IReadOnlyList<HouseholdMembershipSummary> Households,
    Guid? LastActiveHouseholdId);

/// <summary>A household the user belongs to and their role in it.</summary>
public sealed record HouseholdMembershipSummary(Guid HouseholdId, string Name, string Role, bool IsOwner);
