using Budget.Domain.Common;

namespace Budget.Domain.Households;

/// <summary>
/// A group of people sharing a budget, and the unit that owns all budget data (the tenant).
/// Every household has exactly one owner, who is always an Admin.
/// </summary>
public sealed class Household : AuditableEntity
{
    public const string DefaultName = "My Household";
    public const int NameMaxLength = 100;
    public const string DefaultCurrencyCode = "GBP";

    private readonly List<HouseholdMember> _members = [];

    private Household()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = DefaultName;

    /// <summary>ISO 4217 currency code used for the household's money (see ADR-007).</summary>
    public string CurrencyCode { get; private set; } = DefaultCurrencyCode;

    public IReadOnlyCollection<HouseholdMember> Members => _members.AsReadOnly();

    /// <summary>
    /// Creates a household with <paramref name="ownerUserId"/> as its owner and an Admin.
    /// </summary>
    /// <param name="name">The household name; defaults to <see cref="DefaultName"/>.</param>
    public static Household Create(Guid ownerUserId, DateTimeOffset now, string? name = null)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("A household needs an owner.", nameof(ownerUserId));
        }

        var household = new Household
        {
            Id = Guid.CreateVersion7(),
            Name = NormaliseName(name ?? DefaultName),
        };
        household._members.Add(HouseholdMember.CreateOwner(household.Id, ownerUserId, now));

        return household;
    }

    /// <summary>Renames the household. The name is trimmed and must be 1 to <see cref="NameMaxLength"/> characters.</summary>
    public void Rename(string name) => Name = NormaliseName(name);

    /// <summary>Adds someone to the household with the given role. They can't already be a member.</summary>
    /// <remarks>The household's members must be loaded.</remarks>
    public HouseholdMember AddMember(Guid userId, HouseholdRole role, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A member needs a user.", nameof(userId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown household role.");
        }

        if (_members.Any(m => m.UserId == userId))
        {
            throw new InvalidOperationException("That user is already a member of this household.");
        }

        var member = HouseholdMember.Create(Id, userId, role, isOwner: false, now);
        _members.Add(member);
        return member;
    }

    /// <summary>Trims the name and checks it is between 1 and <see cref="NameMaxLength"/> characters.</summary>
    public static string NormaliseName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A household name is required.", nameof(name));
        }

        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"A household name can be at most {NameMaxLength} characters.", nameof(name));
        }

        return trimmed;
    }
}
