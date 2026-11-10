using Budget.Domain.Accounts;

namespace Budget.Domain.Tests.Accounts;

/// <summary>Accounts and pots (BUD-89, ADR-014).</summary>
public class AccountTests
{
    private static readonly Guid Household = Guid.NewGuid();

    [Theory]
    [InlineData("Joint bills", "Joint bills", null)]
    [InlineData("  Chris current  ", "Chris current", null)]
    [InlineData("Joint Monzo Account (Holiday Pot)", "Joint Monzo Account", "Holiday")]
    [InlineData("Joint Monzo Account(garden pot)", "Joint Monzo Account", "garden")]
    [InlineData("Joint Monzo › Food", "Joint Monzo", "Food")]
    [InlineData("Savings (instant access)", "Savings (instant access)", null)]
    [InlineData("(Holiday Pot)", "(Holiday Pot)", null)]
    public void Names_are_read_as_an_account_or_a_pot_in_one(string text, string account, string? pot)
    {
        Assert.Equal((account, pot), AccountNames.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_no_account(string? text) => Assert.Null(AccountNames.Parse(text));

    [Fact]
    public void A_pot_belongs_to_its_accounts_owner_and_shows_both_names()
    {
        var chris = Guid.NewGuid();
        var monzo = Account.Create(Household, " Joint Monzo ", ownerPersonId: null, "Monzo");
        var personal = Account.Create(Household, "Chris current", chris);
        var holiday = Account.CreatePot(monzo, "Holiday");

        Assert.Equal(("Joint Monzo", "Monzo", false, (Guid?)null), (monzo.Name, monzo.Provider, monzo.IsPot, monzo.OwnerPersonId));
        Assert.Equal((Guid?)chris, personal.OwnerPersonId);
        Assert.Equal(((Guid?)null, monzo.Id, true), (holiday.OwnerPersonId, holiday.ParentAccountId, holiday.IsPot));
        Assert.Equal((Guid?)chris, Account.CreatePot(personal, "Savings").OwnerPersonId);
        Assert.Equal("Joint Monzo › Holiday", holiday.DisplayName(monzo));
        Assert.Equal("Joint Monzo", monzo.DisplayName(null));
    }

    [Fact]
    public void Pots_cant_have_pots_and_move_only_into_accounts()
    {
        var jane = Guid.NewGuid();
        var monzo = Account.Create(Household, "Joint Monzo");
        var starling = Account.Create(Household, "Starling", jane);
        var holiday = Account.CreatePot(monzo, "Holiday");

        Assert.Throws<InvalidOperationException>(() => Account.CreatePot(holiday, "Spain"));
        Assert.Throws<InvalidOperationException>(() => starling.MoveTo(monzo));

        holiday.MoveTo(starling);
        Assert.Equal((starling.Id, (Guid?)jane), (holiday.ParentAccountId, holiday.OwnerPersonId));
    }

    [Fact]
    public void Accounts_are_archived_and_restored_not_deleted()
    {
        var account = Account.Create(Household, "Old bank");

        account.Archive();
        Assert.True(account.IsArchived);
        account.Restore();
        Assert.False(account.IsArchived);
    }

    [Fact]
    public void Names_are_required_and_not_too_long()
    {
        Assert.ThrowsAny<ArgumentException>(() => Account.Create(Household, " "));
        Assert.ThrowsAny<ArgumentException>(() => Account.Create(Household, new string('a', Account.NameMaxLength + 1)));
    }
}
