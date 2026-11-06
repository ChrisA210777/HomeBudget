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

    /// <summary>Unlinks the person from their login, e.g. when that member leaves. The person and their income stay.</summary>
    public void Unlink() => LinkedUserId = null;

    /// <summary>
    /// A person for a login, named with just the first name from the sign-in provider, e.g. "Chris" for
    /// "Chris Austin" or "Austin, Chris". It can be renamed afterwards.
    /// </summary>
    public static Person CreateForUser(Guid householdId, Guid userId, string? userDisplayName)
    {
        var name = FirstName(userDisplayName);
        return Create(householdId, name.Length > DisplayNameMaxLength ? name[..DisplayNameMaxLength] : name, userId);
    }

    /// <summary>"Chris Austin" → "Chris"; "Austin, Chris" → "Chris"; nothing → "Me".</summary>
    public static string FirstName(string? fullName)
    {
        var name = fullName?.Trim() ?? string.Empty;

        // Some directories give "Surname, Given names".
        var comma = name.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0 && name[(comma + 1)..].Trim().Length > 0)
        {
            name = name[(comma + 1)..].Trim();
        }

        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "Me" : first;
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
