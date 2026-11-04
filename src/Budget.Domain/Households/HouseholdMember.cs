using Budget.Domain.Common;

namespace Budget.Domain.Households;

/// <summary>A user's membership of a household, with their role in it.</summary>
public sealed class HouseholdMember : AuditableEntity
{
    private HouseholdMember()
    {
    }

    public Guid HouseholdId { get; private set; }

    public Guid UserId { get; private set; }

    public HouseholdRole Role { get; private set; }

    /// <summary>The member who created the household. Always an Admin; can't be removed or demoted.</summary>
    public bool IsOwner { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    internal static HouseholdMember CreateOwner(Guid householdId, Guid userId, DateTimeOffset now) => new()
    {
        HouseholdId = householdId,
        UserId = userId,
        Role = HouseholdRole.Admin,
        IsOwner = true,
        JoinedAt = now,
    };
}
