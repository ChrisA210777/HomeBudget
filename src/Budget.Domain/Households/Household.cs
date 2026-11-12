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

    /// <summary>
    /// "Accounts and transfers" (ADR-014, BUD-91): off by default. When on, transfers between accounts, the
    /// account check and calculated transfers appear. Turning it off hides them but keeps the data.
    /// </summary>
    public bool TransfersEnabled { get; private set; }

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

    /// <summary>Turns "Accounts and transfers" on or off. Only Admins may (checked by the API).</summary>
    public void SetTransfersEnabled(bool enabled) => TransfersEnabled = enabled;

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

    /// <summary>
    /// Changes a member's role. The owner's role can't change, and the household must keep at least one Admin.
    /// </summary>
    /// <remarks>The household's members must be loaded.</remarks>
    /// <exception cref="HouseholdMembershipException">The change isn't allowed; the message explains why.</exception>
    public void ChangeRole(Guid userId, HouseholdRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown household role.");
        }

        var member = FindMember(userId);
        if (member.Role == role)
        {
            return;
        }

        if (member.IsOwner)
        {
            throw new HouseholdMembershipException("The owner's role can't be changed. The owner is always an Admin.");
        }

        if (member.Role == HouseholdRole.Admin && IsLastAdmin(member))
        {
            throw new HouseholdMembershipException("The household needs at least one Admin. Make someone else an Admin first.");
        }

        member.ChangeRole(role);
    }

    /// <summary>
    /// Removes a member, whether an Admin removes them or they leave. The owner can't be removed,
    /// and the household must keep at least one Admin.
    /// </summary>
    /// <remarks>The household's members must be loaded.</remarks>
    /// <exception cref="HouseholdMembershipException">The removal isn't allowed; the message explains why.</exception>
    public void RemoveMember(Guid userId)
    {
        var member = FindMember(userId);
        if (member.IsOwner)
        {
            throw new HouseholdMembershipException(
                "The owner can't leave or be removed. Make another Admin the owner first, or delete the household.");
        }

        if (member.Role == HouseholdRole.Admin && IsLastAdmin(member))
        {
            throw new HouseholdMembershipException("The household needs at least one Admin. Make someone else an Admin first.");
        }

        _members.Remove(member);
    }

    /// <summary>
    /// Hands ownership to another member, who must be an Admin (BUD-157). The previous owner stays as an Admin.
    /// </summary>
    /// <remarks>The household's members must be loaded.</remarks>
    /// <exception cref="HouseholdMembershipException">The change isn't allowed; the message explains why.</exception>
    public void TransferOwnership(Guid toUserId)
    {
        var next = FindMember(toUserId);
        if (next.IsOwner)
        {
            return;
        }

        if (next.Role != HouseholdRole.Admin)
        {
            throw new HouseholdMembershipException("Only an Admin can become the owner. Make them an Admin first.");
        }

        foreach (var member in _members.Where(m => m.IsOwner))
        {
            member.SetOwner(false);
        }

        next.SetOwner(true);
    }

    private HouseholdMember FindMember(Guid userId) =>
        _members.SingleOrDefault(m => m.UserId == userId)
        ?? throw new KeyNotFoundException("That user isn't a member of this household.");

    private bool IsLastAdmin(HouseholdMember member) =>
        !_members.Any(m => m != member && m.Role == HouseholdRole.Admin);

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
