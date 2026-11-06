using Budget.Client.Households;
using Budget.Client.Pages.Households;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Households;

public sealed class CreateHouseholdTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly RecordingHouseholdApi _api = new();

    public CreateHouseholdTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IHouseholdApi>(_api);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    private FakeNavigationManager Navigation => _context.Services.GetRequiredService<FakeNavigationManager>();

    [Fact]
    public void The_name_starts_as_My_Household()
    {
        var cut = _context.RenderComponent<CreateHousehold>();

        Assert.Equal("My Household", cut.Find("input[data-testid=household-name]").GetAttribute("value"));
    }

    [Fact]
    public void Creates_it_and_reloads_the_app_in_the_new_household()
    {
        var cut = _context.RenderComponent<CreateHousehold>();

        cut.Find("input[data-testid=household-name]").Input("  Mum's budget ");
        cut.Find("[data-testid=create]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("  Mum's budget ", Assert.Single(_api.Created).Name);
            Assert.Equal("http://localhost/", Navigation.Uri);
            Assert.True(Navigation.History.First().Options.ForceLoad);
        });
    }

    [Fact]
    public void A_blank_name_is_flagged_before_anything_is_sent()
    {
        var cut = _context.RenderComponent<CreateHousehold>();

        cut.Find("input[data-testid=household-name]").Input("   ");
        cut.Find("[data-testid=create]").Click();

        Assert.Contains("Enter a name for the household.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(_api.Created);
    }

    [Fact]
    public void Errors_from_the_server_are_shown()
    {
        _api.CreateErrors = new() { ["Name"] = ["A household name can be at most 100 characters."] };
        var cut = _context.RenderComponent<CreateHousehold>();

        cut.Find("[data-testid=create]").Click();

        cut.WaitForAssertion(() => Assert.Contains("at most 100 characters", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("http://localhost/", Navigation.Uri);
        Assert.Empty(Navigation.History);
    }

    public void Dispose() => _context.Dispose();
}
