using Budget.Shared.Outgoings;

namespace Budget.Client.Tests.Outgoings;

public sealed class CommonOutgoingsSearchTests
{
    [Fact]
    public void Matches_on_any_word()
    {
        var names = CommonOutgoings.Search("insur").Select(c => c.Name).ToList();

        Assert.Contains("Car insurance", names);
        Assert.Contains("Home contents insurance", names);
        Assert.Contains("Pet insurance", names);
        Assert.DoesNotContain("Netflix", names);
    }

    [Theory]
    [InlineData("TV", "TV Licence")]
    [InlineData("council", "Council tax")]
    [InlineData("prepay", "NHS prescription prepayment certificate")]
    [InlineData("car ins", "Car insurance")]
    public void Finds_common_outgoings_by_what_you_type(string typed, string expected)
    {
        Assert.Contains(expected, CommonOutgoings.Search(typed).Select(c => c.Name));
    }

    [Fact]
    public void Nothing_is_suggested_until_you_type_and_at_most_ten_are()
    {
        Assert.Empty(CommonOutgoings.Search(" "));
        Assert.True(CommonOutgoings.Search("s").Count <= 10);
    }

    [Fact]
    public void An_exact_name_is_found_ignoring_case()
    {
        Assert.Equal("Netflix", CommonOutgoings.Find("  NETFLIX ")!.Name);
        Assert.Null(CommonOutgoings.Find("Netflix and chill"));
    }
}
