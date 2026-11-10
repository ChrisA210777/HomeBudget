using Budget.Server.Features.SignIn;

namespace Budget.Server.Tests.SignIn;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/income")]
    [InlineData("/households/123/members?tab=invites")]
    public void Local_paths_are_kept(string url)
    {
        Assert.Equal(url, ReturnUrl.Sanitize(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("income")]
    [InlineData("https://evil.example.com")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("/income\r\nSet-Cookie: x=y")]
    public void Anything_else_goes_to_the_home_page(string? url)
    {
        Assert.Equal("/", ReturnUrl.Sanitize(url));
    }
}
