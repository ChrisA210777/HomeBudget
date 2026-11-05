using Budget.Domain.Households;

namespace Budget.Domain.Tests.Households;

public class HouseholdTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_household_is_called_My_Household_and_uses_GBP()
    {
        var household = Household.Create(Guid.NewGuid(), Now);

        Assert.Equal("My Household", household.Name);
        Assert.Equal("GBP", household.CurrencyCode);
        Assert.NotEqual(Guid.Empty, household.Id);
    }

    [Fact]
    public void Creator_is_the_only_member_as_owner_and_admin()
    {
        var ownerId = Guid.NewGuid();

        var household = Household.Create(ownerId, Now);

        var member = Assert.Single(household.Members);
        Assert.Equal(household.Id, member.HouseholdId);
        Assert.Equal(ownerId, member.UserId);
        Assert.Equal(HouseholdRole.Admin, member.Role);
        Assert.True(member.IsOwner);
        Assert.Equal(Now, member.JoinedAt);
    }

    [Fact]
    public void Name_can_be_given_and_is_trimmed()
    {
        var household = Household.Create(Guid.NewGuid(), Now, "  Smith Family  ");

        Assert.Equal("Smith Family", household.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name)
    {
        Assert.Throws<ArgumentException>(() => Household.Create(Guid.NewGuid(), Now, name));
    }

    [Fact]
    public void Name_can_be_at_most_100_characters()
    {
        Assert.Equal(100, Household.Create(Guid.NewGuid(), Now, new string('a', 100)).Name.Length);
        Assert.Throws<ArgumentException>(() => Household.Create(Guid.NewGuid(), Now, new string('a', 101)));
    }

    [Fact]
    public void Renaming_trims_the_name_and_keeps_the_rules()
    {
        var household = Household.Create(Guid.NewGuid(), Now);

        household.Rename("  Smith Family ");

        Assert.Equal("Smith Family", household.Name);
        Assert.Throws<ArgumentException>(() => household.Rename(" "));
        Assert.Throws<ArgumentException>(() => household.Rename(new string('a', 101)));
        Assert.Equal("Smith Family", household.Name);
    }

    [Theory]
    [InlineData(HouseholdRole.Viewer)]
    [InlineData(HouseholdRole.User)]
    [InlineData(HouseholdRole.Admin)]
    public void Members_can_be_added_with_any_role_but_are_not_owners(HouseholdRole role)
    {
        var household = Household.Create(Guid.NewGuid(), Now);
        var userId = Guid.NewGuid();

        var member = household.AddMember(userId, role, Now);

        Assert.Equal(2, household.Members.Count);
        Assert.Equal(userId, member.UserId);
        Assert.Equal(household.Id, member.HouseholdId);
        Assert.Equal(role, member.Role);
        Assert.False(member.IsOwner);
        Assert.Single(household.Members, m => m.IsOwner);
    }

    [Fact]
    public void Someone_who_is_already_a_member_cannot_be_added_again()
    {
        var ownerId = Guid.NewGuid();
        var household = Household.Create(ownerId, Now);

        Assert.Throws<InvalidOperationException>(() => household.AddMember(ownerId, HouseholdRole.Viewer, Now));
    }

    [Fact]
    public void Household_needs_an_owner()
    {
        Assert.Throws<ArgumentException>(() => Household.Create(Guid.Empty, Now));
    }
}
