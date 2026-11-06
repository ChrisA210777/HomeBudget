namespace Budget.Shared.Households;

public static class MemberRoutes
{
    /// <summary>GET: the household's members.</summary>
    public static string Members(Guid householdId) => $"/api/households/{householdId}/members";

    /// <summary>DELETE (Admins): remove a member.</summary>
    public static string Member(Guid householdId, Guid userId) => $"/api/households/{householdId}/members/{userId}";

    /// <summary>PUT (Admins): change a member's role.</summary>
    public static string Role(Guid householdId, Guid userId) => $"/api/households/{householdId}/members/{userId}/role";

    /// <summary>DELETE (any member): leave the household.</summary>
    public static string Membership(Guid householdId) => $"/api/households/{householdId}/membership";
}

/// <summary>The roles a member can have, lowest first.</summary>
public static class MemberRoles
{
    public const string Viewer = "Viewer";
    public const string User = "User";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = [Viewer, User, Admin];
}

/// <summary>Change a member's role.</summary>
public sealed record ChangeRoleRequest
{
    /// <summary>Viewer, User or Admin.</summary>
    public string Role { get; init; } = string.Empty;
}

/// <summary>A member of a household.</summary>
/// <param name="Role">Viewer, User or Admin.</param>
public sealed record HouseholdMemberSummary(
    Guid UserId,
    string DisplayName,
    string? Email,
    string Role,
    bool IsOwner,
    DateTimeOffset JoinedAt);
