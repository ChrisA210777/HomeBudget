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
    public void Household_needs_an_owner()
    {
        Assert.Throws<ArgumentException>(() => Household.Create(Guid.Empty, Now));
    }
}
