using Budget.Domain.People;

namespace Budget.Domain.Tests.People;

public class PersonTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();

    [Fact]
    public void A_person_does_not_need_a_login()
    {
        var person = Person.Create(HouseholdId, "  Jane  ");

        Assert.Equal("Jane", person.DisplayName);
        Assert.Equal(HouseholdId, person.HouseholdId);
        Assert.Null(person.LinkedUserId);
        Assert.NotEqual(Guid.Empty, person.Id);
    }

    [Fact]
    public void A_person_for_a_login_is_linked_to_it_and_uses_their_name()
    {
        var userId = Guid.NewGuid();

        var person = Person.CreateForUser(HouseholdId, userId, "Chris Austin");

        Assert.Equal("Chris Austin", person.DisplayName);
        Assert.Equal(userId, person.LinkedUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_login_without_a_name_becomes_Me(string? name)
    {
        Assert.Equal("Me", Person.CreateForUser(HouseholdId, Guid.NewGuid(), name).DisplayName);
    }

    [Fact]
    public void A_long_login_name_is_shortened_to_fit()
    {
        Assert.Equal(100, Person.CreateForUser(HouseholdId, Guid.NewGuid(), new string('a', 150)).DisplayName.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Name_is_required(string name)
    {
        Assert.Throws<ArgumentException>(() => Person.Create(HouseholdId, name));
        Assert.Throws<ArgumentException>(() => Person.Create(HouseholdId, "Jane").Rename(name));
    }

    [Fact]
    public void Name_can_be_at_most_100_characters()
    {
        Assert.Throws<ArgumentException>(() => Person.Create(HouseholdId, new string('a', 101)));
    }

    [Fact]
    public void Renaming_trims_the_name()
    {
        var person = Person.Create(HouseholdId, "Jane");

        person.Rename(" Jane Austin ");

        Assert.Equal("Jane Austin", person.DisplayName);
    }
}
