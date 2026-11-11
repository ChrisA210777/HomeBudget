namespace Budget.Client.Tests;

public sealed class BackLinkTests
{
    [Theory]
    [InlineData("/outgoings/1", "/budget", "/outgoings/1?from=%2Fbudget")]
    [InlineData("/outgoings/1", "/outgoings?month=2026-11&kind=Bill", "/outgoings/1?from=%2Foutgoings%3Fmonth%3D2026-11%26kind%3DBill")]
    [InlineData("/outgoings/new?x=1", "/", "/outgoings/new?x=1&from=%2F")]
    [InlineData("/outgoings/1", null, "/outgoings/1")]
    [InlineData("/outgoings/1", "https://evil.example", "/outgoings/1")]
    public void Remembers_a_safe_origin_in_the_link(string href, string? from, string expected) =>
        Assert.Equal(expected, BackLink.To(href, from));

    [Theory]
    [InlineData("/cash-flow", "/cash-flow")]
    [InlineData(null, "/income")]
    [InlineData("", "/income")]
    [InlineData("//evil.example", "/income")]
    [InlineData("/\\evil.example", "/income")]
    [InlineData("javascript:alert(1)", "/income")]
    [InlineData("income", "/income")]
    public void Only_goes_back_within_the_app(string? from, string expected) =>
        Assert.Equal(expected, BackLink.Resolve(from, "/income"));

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/outgoings?month=2026-11", "Outgoings")]
    [InlineData("/outgoings/upcoming", "Upcoming payments")]
    [InlineData("/cash-flow/", "Cash flow")]
    [InlineData("/outgoings/123", null)]
    public void Names_the_page_for_the_back_button(string href, string? expected) =>
        Assert.Equal(expected, BackLink.Name(href));
}
