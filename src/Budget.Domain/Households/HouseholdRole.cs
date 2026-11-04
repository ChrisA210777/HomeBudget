namespace Budget.Domain.Households;

/// <summary>
/// A member's role within a household. Roles are ordered: each role includes the
/// permissions of the roles below it (Viewer &lt; User &lt; Admin).
/// Ownership is recorded separately on the membership, not as a role.
/// </summary>
public enum HouseholdRole
{
    /// <summary>Can view household data but not change it.</summary>
    Viewer = 0,

    /// <summary>Can add and edit budget data such as income streams.</summary>
    User = 1,

    /// <summary>Can also manage the household and its members.</summary>
    Admin = 2,
}

public static class HouseholdRoleExtensions
{
    /// <summary>
    /// Returns true when <paramref name="role"/> grants at least the permissions of
    /// <paramref name="required"/>.
    /// </summary>
    public static bool Satisfies(this HouseholdRole role, HouseholdRole required)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown household role.");
        }

        if (!Enum.IsDefined(required))
        {
            throw new ArgumentOutOfRangeException(nameof(required), required, "Unknown household role.");
        }

        return role >= required;
    }
}
