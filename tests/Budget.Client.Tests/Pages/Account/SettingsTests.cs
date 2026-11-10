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

    public SettingsTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IUserSettingsApi>(_api);
        _context.Services.AddScoped<UserSettingsStore>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Shows_the_saved_settings()
    {
        _api.Saved = new UserSettings(DayOfWeek.Monday, ListPeriodDto.Year, OutgoingsGroupingDto.PaidFrom, ListPeriodDto.Month);

        var cut = _context.RenderComponent<Settings>();

        // What each select shows.
        Assert.Equal("Monday", Shown(cut, "first-day-of-week"));
        Assert.Equal("12 months", Shown(cut, "outgoings-period"));
        Assert.Equal("Paid from", Shown(cut, "outgoings-grouping"));
        Assert.Equal("A month", Shown(cut, "income-period"));
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

    public void Dispose() => _context.Dispose();

    private static string? Shown(IRenderedComponent<Settings> cut, string testId) =>
        cut.Find($"input[data-testid={testId}]").GetAttribute("value");
}
