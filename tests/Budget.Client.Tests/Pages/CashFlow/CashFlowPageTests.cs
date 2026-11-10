using Budget.Client.Account;
using Budget.Client.CashFlow;
using Budget.Client.Pages.CashFlow;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.CashFlow;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.CashFlow;

/// <summary>The Cash flow page (BUD-92).</summary>
public sealed class CashFlowPageTests : IDisposable
{
    private static readonly Guid JointId = Guid.NewGuid();
    private static readonly Guid CurrentId = Guid.NewGuid();
    private readonly TestContext _context = new();
    private readonly FakeCashFlowApi _api = new();

    public CashFlowPageTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<ICashFlowApi>(_api);
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddSingleton<IUserSettingsApi>(new FakeUserSettingsApi());
        _context.Services.AddScoped<UserSettingsStore>();
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Shows_each_accounts_month_with_a_shortfall_in_words()
    {
        UseTransfers(true);

        var cut = _context.RenderComponent<CashFlowPage>();

        Assert.Equal("November 2026", Text(cut, "period-name"));
        Assert.Equal(new[] { "Current", "Joint" }, Texts(cut, "account-name"));
        Assert.Equal(new[] { "Surplus £2,000.00", "Short by £150.00" }, Texts(cut, "account-net"));
        Assert.Equal(new[] { "In £3,000.00 · Out £1,000.00", "In £1,000.00 · Out £1,150.00" }, Texts(cut, "account-totals"));
        Assert.Contains("Mortgage · Bills pot", Texts(cut, "line-name"));
        Assert.Equal((2026, 11), _api.Months.Single());
    }

    [Fact]
    public void Moves_a_month_at_a_time()
    {
        UseTransfers(true);
        var cut = _context.RenderComponent<CashFlowPage>();

        cut.Find("[data-testid=next-period]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { (2026, 11), (2026, 12) }, _api.Months));
        Assert.Equal("December 2026", Text(cut, "period-name"));
    }

    [Fact]
    public void Day_by_day_shows_running_totals_and_flags_the_timing_gap()
    {
        UseTransfers(true);
        var cut = _context.RenderComponent<CashFlowPage>();

        cut.Find("[data-testid=show-daily]").Click();

        cut.WaitForAssertion(() => Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), _api.Days.Single()));
        // The first account is shown to start with.
        Assert.Equal(new[] { "£2,000.00 so far" }, Texts(cut, "day-running"));

        cut.InvokeAsync(() => cut.FindComponent<MudBlazor.MudSelect<Guid?>>().Instance.ValueChanged.InvokeAsync(JointId));

        cut.WaitForAssertion(() => Assert.Equal(new[] { "Short by £1,000.00", "Short by £150.00" }, Texts(cut, "day-short")));
        Assert.Contains("1 Nov 2026", Text(cut, "timing-gap"), StringComparison.Ordinal);
    }

    [Fact]
    public void Says_how_to_turn_it_on_when_transfers_are_off()
    {
        UseTransfers(false);

        var cut = _context.RenderComponent<CashFlowPage>();

        Assert.NotNull(cut.Find("[data-testid=cash-flow-off]"));
        Assert.Empty(_api.Months);
    }

    public void Dispose() => _context.Dispose();

    private void UseTransfers(bool enabled) => _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("User", false, enabled));

    private static string Text(IRenderedComponent<CashFlowPage> cut, string testId) => Normalise(cut.Find($"[data-testid={testId}]").TextContent);

    private static string[] Texts(IRenderedComponent<CashFlowPage> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => Normalise(e.TextContent)).ToArray();

    private static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class FakeCashFlowApi : ICashFlowApi
    {
        public List<(int Year, int Month)> Months { get; } = [];

        public List<(DateOnly From, DateOnly To)> Days { get; } = [];

        public Task<CashFlowMonthResponse> GetMonthAsync(Guid householdId, int year, int month)
        {
            Months.Add((year, month));
            return Task.FromResult(new CashFlowMonthResponse(year, month, "GBP",
            [
                new(CurrentId, "Current",
                    [new(CashFlowKindDto.Income, Guid.NewGuid(), "Salary", null, 3000m)],
                    [new(CashFlowKindDto.TransferOut, Guid.NewGuid(), "Bills top-up", null, 1000m)], 3000m, 1000m),
                new(JointId, "Joint",
                    [new(CashFlowKindDto.TransferIn, Guid.NewGuid(), "Bills top-up", "Bills", 1000m)],
                    [new(CashFlowKindDto.Outgoing, Guid.NewGuid(), "Mortgage", "Bills", 1000m), new(CashFlowKindDto.Outgoing, Guid.NewGuid(), "Council tax", null, 150m)],
                    1000m, 1150m),
            ]));
        }

        public Task<CashFlowDaysResponse> GetDaysAsync(Guid householdId, DateOnly from, DateOnly to)
        {
            Days.Add((from, to));
            return Task.FromResult(new CashFlowDaysResponse(from, to, "GBP",
            [
                new(CurrentId, "Current",
                [
                    new(new DateOnly(2026, 11, 25),
                        [new(CashFlowKindDto.Income, Guid.NewGuid(), "Salary", null, 3000m), new(CashFlowKindDto.TransferOut, Guid.NewGuid(), "Bills top-up", null, 1000m)],
                        3000m, 1000m, 2000m, false),
                ], 3000m, 1000m),
                new(JointId, "Joint",
                [
                    new(new DateOnly(2026, 11, 1), [new(CashFlowKindDto.Outgoing, Guid.NewGuid(), "Mortgage", "Bills", 1000m)], 0m, 1000m, -1000m, true),
                    new(new DateOnly(2026, 11, 25), [new(CashFlowKindDto.TransferIn, Guid.NewGuid(), "Bills top-up", "Bills", 1000m), new(CashFlowKindDto.Outgoing, Guid.NewGuid(), "Council tax", null, 150m)], 1000m, 150m, -150m, true),
                ], 1000m, 1150m),
            ]));
        }
    }
}
