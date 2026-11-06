using Budget.Client.Households;
using Budget.Client.Layout;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Account;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Layout;

public sealed class HouseholdSwitcherTests : IDisposable
{
    private static readonly HouseholdMembershipSummary Ours = new(Guid.NewGuid(), "Austin Family", "Admin", IsOwner: true);
    private static readonly HouseholdMembershipSummary Mums = new(Guid.NewGuid(), "Mum's budget", "User", IsOwner: false);

    private readonly TestContext _context = new();
    private readonly RecordingHouseholdApi _api = new();

    public HouseholdSwitcherTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IHouseholdApi>(_api);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private FakeNavigationManager Navigation => _context.Services.GetRequiredService<FakeNavigationManager>();

    [Fact]
    public void Shows_the_active_household_and_shortens_long_names()
    {
        var cut = Render(Ours);
        Assert.Equal("Austin Family", cut.Find("[data-testid=appbar-household] button").TextContent.Trim());

        var longName = new HouseholdMembershipSummary(Guid.NewGuid(), "The Very Long Household Name Of Everyone", "Admin", true);
        var shortened = _context.RenderComponent<HouseholdSwitcher>(p => p.Add(c => c.Active, longName).Add(c => c.Households, new[] { longName }));
        Assert.Equal("The Very Long Household…", shortened.Find("[data-testid=appbar-household] button").TextContent.Trim());
    }

    [Fact]
    public void Lists_every_household_with_your_role_and_marks_the_current_one()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = Render(Ours);

        cut.Find("[data-testid=appbar-household] button").Click();

        var items = popovers.WaitForElements("[data-testid=switch-household]").ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains("Austin Family", items[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Owner", items[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("true", items[0].GetAttribute("data-active"));
        Assert.Contains("Mum's budget", items[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("User", items[1].TextContent, StringComparison.Ordinal);
        Assert.Equal("false", items[1].GetAttribute("data-active"));
        Assert.NotNull(popovers.Find("[data-testid=create-household]"));
    }

    [Fact]
    public void Switching_saves_the_choice_and_reloads_the_app_at_home()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = Render(Ours);
        Navigation.NavigateTo("/outgoings");

        cut.Find("[data-testid=appbar-household] button").Click();
        popovers.WaitForElements("[data-testid=switch-household]")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal([Mums.HouseholdId], _api.SwitchedTo);
            Assert.Equal("http://localhost/", Navigation.Uri);
            Assert.True(Navigation.History.First().Options.ForceLoad);
        });
    }

    [Fact]
    public void Choosing_the_current_household_does_nothing()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = Render(Ours);

        cut.Find("[data-testid=appbar-household] button").Click();
        popovers.WaitForElements("[data-testid=switch-household]")[0].Click();

        Assert.Empty(_api.SwitchedTo);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<HouseholdSwitcher> Render(HouseholdMembershipSummary active) =>
        _context.RenderComponent<HouseholdSwitcher>(p => p.Add(c => c.Active, active).Add(c => c.Households, new[] { Ours, Mums }));
}
