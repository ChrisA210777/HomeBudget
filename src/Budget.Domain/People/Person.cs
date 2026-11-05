using Budget.Domain.Common;
using Budget.Domain.Households;

namespace Budget.Domain.People;

/// <summary>
/// Someone in the household whose income (and later bills) we track. Separate from logins: a
/// person may never sign in, and can be linked to a login later (see ADR-009).
/// </summary>
public sealed class Person : AuditableEntity, IHouseholdOwned
{
    public const int DisplayNameMaxLength = 100;

    private Person()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The login this person signs in with, if any. At most one person per login per household.</summary>
    public Guid? LinkedUserId { get; private set; }

    public static Person Create(Guid householdId, string displayName, Guid? linkedUserId = null)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException("A person must belong to a household.", nameof(householdId));
        }

        return new Person
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            DisplayName = NormaliseName(displayName),
            LinkedUserId = linkedUserId == Guid.Empty ? null : linkedUserId,
        };
    }

    /// <summary>A person for a login, using their name from the sign-in provider (shortened if needed).</summary>
    public static Person CreateForUser(Guid householdId, Guid userId, string? userDisplayName)
    {
        var name = string.IsNullOrWhiteSpace(userDisplayName) ? "Me" : userDisplayName.Trim();
        return Create(householdId, name.Length > DisplayNameMaxLength ? name[..DisplayNameMaxLength] : name, userId);
    }

    public void Rename(string displayName) => DisplayName = NormaliseName(displayName);

    public static string NormaliseName(string displayName)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A name is required.", nameof(displayName));
        }

        if (trimmed.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException($"A name can be at most {DisplayNameMaxLength} characters.", nameof(displayName));
        }

        return trimmed;
    }
}
