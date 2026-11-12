using Budget.Client.Account;
using Budget.Client.Pages.Account;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Account;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Account;

/// <summary>The user's own settings page (BUD-87).</summary>
public sealed class SettingsTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakeUserSettingsApi _api = new();
    private readonly FakeDeletionApi _deletion = new();

    public SettingsTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IUserSettingsApi>(_api);
        _context.Services.AddScoped<UserSettingsStore>();
        _context.Services.AddSingleton<IAccountDeletionApi>(_deletion);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Shows_the_saved_settings()
    {
        _api.Saved = new UserSettings(DayOfWeek.Monday, ListPeriodDto.Year, OutgoingsGroupingDto.PaidFrom, ListPeriodDto.Month);

        var cut = _context.RenderComponent<Settings>();

        // Reading Value directly is fine in a test that only checks what the page passed in.
#pragma warning disable MUD0012
        Assert.Equal(DayOfWeek.Monday, cut.FindComponent<MudSelect<DayOfWeek>>().Instance.Value);
        var periods = cut.FindComponents<MudSelect<ListPeriodDto>>().ToList();
        Assert.Equal((ListPeriodDto.Year, ListPeriodDto.Month), (periods[0].Instance.Value, periods[1].Instance.Value));
        Assert.Equal(OutgoingsGroupingDto.PaidFrom, cut.FindComponent<MudSelect<OutgoingsGroupingDto>>().Instance.Value);
#pragma warning restore MUD0012
    }

    [Fact]
    public async Task A_change_is_saved_on_its_own()
    {
        var cut = _context.RenderComponent<Settings>();

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<DayOfWeek>>().Instance.ValueChanged.InvokeAsync(DayOfWeek.Monday));

        var update = Assert.Single(_api.Updates);
        Assert.Equal((DayOfWeek?)DayOfWeek.Monday, update.FirstDayOfWeek);
        Assert.Null(update.OutgoingsPeriod);
        Assert.Equal(DayOfWeek.Monday, _api.Saved.FirstDayOfWeek);
    }

    [Fact]
    public async Task A_change_that_cant_be_saved_still_applies_for_now()
    {
        var cut = _context.RenderComponent<Settings>();
        _api.Offline = true;

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<OutgoingsGroupingDto>>().Instance.ValueChanged.InvokeAsync(OutgoingsGroupingDto.PaidFrom));

        Assert.Equal(OutgoingsGroupingDto.PaidFrom, _context.Services.GetRequiredService<UserSettingsStore>().Current.OutgoingsGrouping);
        Assert.Equal(OutgoingsGroupingDto.Category, _api.Saved.OutgoingsGrouping);
    }

    [Fact]
    public void Deleting_the_account_says_what_happens_and_needs_DELETE_typed()
    {
        _deletion.Check = new AccountDeletionCheck([new OwnedHousehold(Guid.NewGuid(), "Just me")], [], HouseholdsToLeave: 2);
        var cut = _context.RenderComponent<Settings>();
        var navigation = _context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        cut.Find("[data-testid=delete-account]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Just me", cut.Find("[data-testid=households-to-delete]").TextContent, StringComparison.Ordinal));
        Assert.Contains("2 households", cut.Find("[data-testid=households-to-leave]").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("[data-testid=delete-account-yes]").HasAttribute("disabled"));

        cut.Find("input[data-testid=delete-account-confirm]").Input(" delete ");
        cut.Find("[data-testid=delete-account-yes]").Click();

        cut.WaitForAssertion(() => Assert.Equal(" delete ", _deletion.Confirmed));
        Assert.Equal("http://localhost/login?accountDeleted=true", navigation.Uri);
    }

    [Fact]
    public void Says_who_becomes_owner_of_households_others_use()
    {
        _deletion.Check = new AccountDeletionCheck(
            [], [new PassedOnHousehold(Guid.NewGuid(), "The Austins", "Jane", BecomesAdmin: false), new PassedOnHousehold(Guid.NewGuid(), "Flat", "Sam", BecomesAdmin: true)], 0);
        var cut = _context.RenderComponent<Settings>();

        cut.Find("[data-testid=delete-account]").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=household-to-pass-on]").Count));
        var passed = cut.FindAll("[data-testid=household-to-pass-on]").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal("makes Jane the owner of The Austins", passed[0]);
        Assert.Equal("makes Sam the owner of Flat, and an Admin", passed[1]);
        Assert.NotNull(cut.Find("[data-testid=delete-account-yes]"));
    }

    [Fact]
    public void A_refusal_from_the_server_is_shown_and_nothing_navigates()
    {
        _deletion.Check = new AccountDeletionCheck([], [], 0);
        _deletion.Refusal = "Your account couldn't be deleted.";
        var cut = _context.RenderComponent<Settings>();
        var navigation = _context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        cut.Find("[data-testid=delete-account]").Click();
        cut.WaitForAssertion(() => cut.Find("input[data-testid=delete-account-confirm]").Input("DELETE"));
        cut.Find("[data-testid=delete-account-yes]").Click();

        cut.WaitForAssertion(() => Assert.Contains("couldn't be deleted", cut.Find("[data-testid=delete-account-error]").TextContent, StringComparison.Ordinal));
        Assert.Equal("http://localhost/", navigation.Uri);
    }

    public void Dispose() => _context.Dispose();

    private sealed class FakeDeletionApi : IAccountDeletionApi
    {
        public AccountDeletionCheck Check { get; set; } = new([], [], 0);

        public string? Refusal { get; set; }

        public string? Confirmed { get; private set; }

        public Task<AccountDeletionCheck> CheckAsync() => Task.FromResult(Check);

        public Task<string?> DeleteAsync(string confirmation)
        {
            Confirmed = confirmation;
            return Task.FromResult(Refusal);
        }
    }
}
