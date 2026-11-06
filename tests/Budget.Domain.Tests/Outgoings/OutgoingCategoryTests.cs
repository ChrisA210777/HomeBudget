using Budget.Domain.Outgoings;

namespace Budget.Domain.Tests.Outgoings;

/// <summary>Category icons (BUD-69).</summary>
public class OutgoingCategoryTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();

    [Fact]
    public void Default_categories_start_with_their_own_icons()
    {
        var categories = OutgoingCategory.CreateDefaults(HouseholdId);

        Assert.Equal("home", categories.Single(c => c.Name == "Housing").Icon);
        Assert.Equal("bolt", categories.Single(c => c.Name == "Utilities").Icon);
        Assert.DoesNotContain(categories, c => c.Icon == OutgoingCategory.DefaultIcon);
    }

    [Fact]
    public void Other_categories_get_a_plain_label_unless_an_icon_is_chosen()
    {
        Assert.Equal(OutgoingCategory.DefaultIcon, OutgoingCategory.Create(HouseholdId, "Hobbies", 10).Icon);
        Assert.Equal("coffee", OutgoingCategory.Create(HouseholdId, "Hobbies", 10, "coffee").Icon);
        Assert.Equal("home", OutgoingCategory.Create(HouseholdId, " housing ", 10).Icon);
    }

    [Fact]
    public void The_icon_can_be_changed()
    {
        var category = OutgoingCategory.Create(HouseholdId, "Hobbies", 10);

        category.ChangeIcon("music");

        Assert.Equal("music", category.Icon);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this-icon-key-is-far-too-long-to-store")]
    public void Blank_or_overlong_icon_keys_are_refused(string icon)
    {
        var category = OutgoingCategory.Create(HouseholdId, "Hobbies", 10);

        Assert.Throws<ArgumentException>(() => category.ChangeIcon(icon));
    }
}
