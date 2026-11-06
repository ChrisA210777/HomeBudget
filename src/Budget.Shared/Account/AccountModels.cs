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
/// <param name="PersonId">The person in that household linked to the user's login, if any.</param>
/// <param name="PersonName">That person's name, e.g. "Chris"; shown instead of the login's full name.</param>
public sealed record HouseholdMembershipSummary(Guid HouseholdId, string Name, string Role, bool IsOwner, Guid? PersonId = null, string? PersonName = null);

/// <summary>Switch to another of the user's households.</summary>
public sealed record SetActiveHouseholdRequest(Guid HouseholdId);
