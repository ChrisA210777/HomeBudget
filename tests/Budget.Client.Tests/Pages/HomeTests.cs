using Budget.Client.Pages;
using Bunit.TestDoubles;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages;

public sealed class HomeTests : IDisposable
{
    private readonly TestContext _context = new();

    public HomeTests()
    {
        _context.Services.AddMudServices();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Home_page_shows_app_heading()
    {
        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Home Budget", cut.Find("h1").TextContent.Trim());
    }

    [Fact]
    public void Home_page_welcomes_the_signed_in_user_by_name()
    {
        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Welcome, Chris Austin", cut.Find("[data-testid=welcome]").TextContent.Trim());
    }

    public void Dispose() => _context.Dispose();
}
