namespace Budget.Shared.Households;

public static class MemberRoutes
{
    /// <summary>GET: the household's members.</summary>
    public static string Members(Guid householdId) => $"/api/households/{householdId}/members";
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
