namespace Budget.Shared.Households;

public static class HouseholdRoutes
{
    /// <summary>All household-scoped API routes start with this (see Roles &amp; Permissions).</summary>
    public const string Base = "/api/households/{householdId:guid}";

    /// <summary>GET: the household's details and the caller's role in it.</summary>
    public static string Household(Guid householdId) => $"/api/households/{householdId}";
}

/// <summary>A household as seen by one of its members.</summary>
/// <param name="Role">The caller's role: Viewer, User or Admin.</param>
public sealed record HouseholdResponse(
    Guid Id,
    string Name,
    string CurrencyCode,
    string Role,
    bool IsOwner,
    int MemberCount);
