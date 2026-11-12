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

    /// <summary>
    /// The member who owns the household: whoever created it, unless they've handed it on (BUD-157). Always an Admin;
    /// can't be removed or demoted.
    /// </summary>
    public bool IsOwner { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    internal void ChangeRole(HouseholdRole role) => Role = role;

    internal void SetOwner(bool isOwner) => IsOwner = isOwner;

    internal static HouseholdMember CreateOwner(Guid householdId, Guid userId, DateTimeOffset now) =>
        Create(householdId, userId, HouseholdRole.Admin, isOwner: true, now);

    internal static HouseholdMember Create(Guid householdId, Guid userId, HouseholdRole role, bool isOwner, DateTimeOffset now) => new()
    {
        HouseholdId = householdId,
        UserId = userId,
        Role = role,
        IsOwner = isOwner,
        JoinedAt = now,
    };
}
