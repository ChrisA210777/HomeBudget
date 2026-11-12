using Budget.Client.Account;
using Budget.Client.Pages;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages;

public sealed class MorePageTests : IDisposable
{
    private readonly TestContext _context = new();

    public MorePageTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Lists_the_pages_not_in_the_bottom_bar_in_groups()
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(transfersEnabled: true));

        var cut = _context.RenderComponent<MorePage>();

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { "Upcoming payments", "Renewals", "Cash flow", "Accounts", "People", "Members", "Recent activity", "Import or export data", "Settings" },
            cut.FindAll("[data-testid=more-link]").Select(a => a.TextContent.Trim()).ToArray()));
        Assert.Equal(new[] { "Planning", "Money", "Household", "You" }, cut.FindAll("h2").Select(h => h.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void Viewers_without_transfers_see_fewer_pages()
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role: "Viewer", isOwner: false));

        var cut = _context.RenderComponent<MorePage>();

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { "Upcoming payments", "Renewals", "Accounts", "People", "Members", "Recent activity", "Settings" },
            cut.FindAll("[data-testid=more-link]").Select(a => a.TextContent.Trim()).ToArray()));
    }

    public void Dispose() => _context.Dispose();
}
