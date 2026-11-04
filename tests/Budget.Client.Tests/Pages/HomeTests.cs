using Budget.Client.Pages;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages;

public sealed class HomeTests : IDisposable
{
    private readonly TestContext _context = new();

    public HomeTests()
    {
        _context.Services.AddMudServices();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_page_shows_app_heading()
    {
        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Home Budget", cut.Find("h1").TextContent.Trim());
    }

    public void Dispose() => _context.Dispose();
}
