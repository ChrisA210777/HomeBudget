using Budget.Client.Account;
using Budget.Client.Pages.Households;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Households;

public sealed class HouseholdDataTests : IDisposable
{
    private readonly TestContext _context = new();

    public HouseholdDataTests()
    {
        _context.Services.AddMudServices();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Admins_download_the_core_data_by_default_with_a_warning()
    {
        UseRole("Admin");

        var cut = _context.RenderComponent<HouseholdData>();

        var link = cut.Find("a[data-testid=download-json]");
        Assert.Equal($"/api/households/{FakeAccountApi.HouseholdId}/export?history=false&audit=false", link.GetAttribute("href"));
        Assert.True(link.HasAttribute("download"));
        Assert.Contains("personal and financial details", cut.Find("[data-testid=export-warning]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticking_the_options_adds_history_and_audit_details()
    {
        UseRole("Admin");
        var cut = _context.RenderComponent<HouseholdData>();

        cut.Find("[data-testid=include-history] input").Change(true);
        cut.Find("[data-testid=include-audit] input").Change(true);

        Assert.EndsWith("/export?history=true&audit=true", cut.Find("a[data-testid=download-json]").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Viewer")]
    public void Only_admins_can_download(string role)
    {
        UseRole(role, isOwner: false);

        var cut = _context.RenderComponent<HouseholdData>();

        Assert.NotNull(cut.Find("[data-testid=admins-only]"));
        Assert.Empty(cut.FindAll("[data-testid=export-section]"));
    }

    public void Dispose() => _context.Dispose();

    private void UseRole(string role, bool isOwner = true)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
    }
}
