using Budget.Domain.Common;
using Budget.Domain.Households;

namespace Budget.Domain.Accounts;

/// <summary>
/// A bank account, or a pot within one, that money is paid from or into (BUD-89, ADR-014). A pot is an account
/// with a <see cref="ParentAccountId"/>, one level down only, and belongs to its account's owner. Accounts are archived
/// rather than deleted, so history still makes sense; archived ones aren't offered for new entries. Names are
/// unique among an account's pots, and among a household's accounts (the database ignores case).
/// </summary>
public sealed class Account : AuditableEntity, IHouseholdOwned
{
    public const int NameMaxLength = 100;
    public const int ProviderMaxLength = 100;

    /// <summary>Shown between an account and its pot, e.g. "Joint Monzo › Holiday".</summary>
    public const string PotSeparator = " › ";

    private Account()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The bank, e.g. "Monzo".</summary>
    public string? Provider { get; private set; }

    /// <summary>
    /// The person in the household whose account it is, or <c>null</c> for the household's own (joint) account.
    /// In a private household this decides who can see it (BUD-94).
    /// </summary>
    public Guid? OwnerPersonId { get; private set; }

    /// <summary>The account this is a pot in, or <c>null</c> for an account.</summary>
    public Guid? ParentAccountId { get; private set; }

    public bool IsArchived { get; private set; }

    /// <summary>
    /// The income or transfer whose arrival starts this account's month (BUD-136), or <c>null</c> for the 1st. Cash flow
    /// starts the account's running total again each time it arrives. Not a foreign key: if the payment is deleted,
    /// the account simply goes back to the 1st. Always <c>null</c> for a pot, which follows its account.
    /// </summary>
    public Guid? MonthStartsWithId { get; private set; }

    public bool IsPot => ParentAccountId is not null;

    /// <param name="ownerPersonId">A person in the household, or <c>null</c> for the household's own account.</param>
    public static Account Create(Guid householdId, string name, Guid? ownerPersonId = null, string? provider = null) => new()
    {
        Id = Guid.NewGuid(),
        HouseholdId = householdId,
        Name = Values.Required(name, nameof(name), NameMaxLength),
        Provider = Values.Optional(provider, nameof(provider), ProviderMaxLength),
        OwnerPersonId = ownerPersonId,
    };

    /// <summary>A pot in <paramref name="account"/>, which can't itself be a pot.</summary>
    public static Account CreatePot(Account account, string name)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.IsPot)
        {
            throw new InvalidOperationException("Pots can't have pots of their own.");
        }

        return new Account
        {
            Id = Guid.NewGuid(),
            HouseholdId = account.HouseholdId,
            Name = Values.Required(name, nameof(name), NameMaxLength),
            OwnerPersonId = account.OwnerPersonId,
            ParentAccountId = account.Id,
        };
    }

    /// <summary>"Account" or "Account › Pot".</summary>
    public string DisplayName(Account? parent) => parent is null ? Name : $"{parent.Name}{PotSeparator}{Name}";

    public void Update(string name, Guid? ownerPersonId, string? provider)
    {
        Name = Values.Required(name, nameof(name), NameMaxLength);
        OwnerPersonId = ownerPersonId;
        Provider = IsPot ? null : Values.Optional(provider, nameof(provider), ProviderMaxLength);
    }

    /// <summary>A pot belongs to its account's owner.</summary>
    public void FollowOwner(Guid? ownerPersonId) => OwnerPersonId = ownerPersonId;

    /// <summary>Moves a pot into another account, e.g. when two accounts are merged.</summary>
    public void MoveTo(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!IsPot || account.IsPot || account.HouseholdId != HouseholdId)
        {
            throw new InvalidOperationException("Only a pot can move, and only into an account in the same household.");
        }

        ParentAccountId = account.Id;
        OwnerPersonId = account.OwnerPersonId;
    }

    /// <summary>Starts the account's month when the given income or transfer arrives, or on the 1st for <c>null</c>.</summary>
    public void StartMonthWith(Guid? paymentId)
    {
        if (IsPot && paymentId is not null)
        {
            throw new InvalidOperationException("A pot's month follows its account.");
        }

        MonthStartsWithId = paymentId;
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;
}
