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
        var popovers = _context.RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = _context.RenderComponent<CashFlowPage>();

        cut.Find("[data-testid=show-daily]").Click();

        cut.WaitForAssertion(() => Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), _api.Days.Single()));
        // The first account is shown to start with.
        Assert.Equal(new[] { "Running total £2,000.00" }, Texts(cut, "day-running"));

        // BUD-130: the account is a compact menu.
        cut.Find("[data-testid=day-account] button").Click();
        popovers.WaitForElements("[data-testid=day-account-option]").Single(o => o.TextContent.Trim() == "Joint").Click();
        cut.WaitForAssertion(() => Assert.Equal("Joint", Text(cut, "day-account")));

        cut.WaitForAssertion(() => Assert.Equal(new[] { "Short by £1,000.00", "Short by £150.00" }, Texts(cut, "day-short")));
        Assert.Contains("1 Nov 2026", Text(cut, "timing-gap"), StringComparison.Ordinal);
    }

    [Fact]
    public void Carries_on_from_the_accounts_month_and_marks_where_it_starts_again()
    {
        UseTransfers(true);
        var topUp = Guid.NewGuid();
        _api.DaysResponse = (from, to) => new CashFlowDaysResponse(from, to, "GBP",
        [
            new(JointId, "Joint",
            [
                new(new DateOnly(2026, 11, 5), [new(CashFlowKindDto.Outgoing, Guid.NewGuid(), "Council tax", null, 150m)], 0m, 150m, -150m, true),
                new(new DateOnly(2026, 11, 25), [new(CashFlowKindDto.TransferIn, topUp, "Bills top-up", "Bills", 1000m)], 1000m, 0m, 1000m, false)
                {
                    MonthStarted = new DateOnly(2026, 11, 25),
                },
            ], 1000m, 150m)
            {
                BroughtForward = 0m,
                BroughtForwardSince = new DateOnly(2026, 10, 25),
                MonthStartsWith = "Bills top-up",
            },
        ]);
        var cut = _context.RenderComponent<CashFlowPage>();

        cut.Find("[data-testid=show-daily]").Click();

        // BUD-136: the running totals carry on from the account's month, which starts again when the top-up arrives.
        cut.WaitForAssertion(() => Assert.Equal("Brought forward since 25 Oct £0.00", Text(cut, "brought-forward")));
        Assert.Equal(new[] { "Month starts · Bills top-up" }, Texts(cut, "month-start"));
        Assert.Equal(new[] { "Short by £150.00" }, Texts(cut, "day-short"));
        Assert.Equal(new[] { "Running total £1,000.00" }, Texts(cut, "day-running"));
        Assert.Equal("Surplus £1,000.00", Text(cut, "account-net"));
        Assert.Empty(cut.FindAll("[data-testid=month-start-tip]"));
    }

    [Fact]
    public void A_month_shows_each_accounts_own_month_and_says_which_days()
    {
        UseTransfers(true);
        _api.DaysResponse = (from, to) => new CashFlowDaysResponse(from, to, "GBP",
        [
            new(JointId, "Joint",
            [
                new(new DateOnly(2026, 10, 21), [new(CashFlowKindDto.Income, Guid.NewGuid(), "Salary", null, 3000m)], 3000m, 0m, 3000m, false)
                {
                    MonthStarted = new DateOnly(2026, 10, 21),
                },
            ], 3000m, 0m)
            {
                From = new DateOnly(2026, 10, 21),
                To = new DateOnly(2026, 11, 20),
                MonthStartsWith = "Salary",
            },
        ]);
        var cut = _context.RenderComponent<CashFlowPage>();
        cut.Find("[data-testid=show-daily]").Click();

        // BUD-138: the month view asks for each account's own month, and says which days that is.
        cut.WaitForAssertion(() => Assert.Equal(new[] { true }, _api.AccountMonths));
        Assert.Equal("This account's month: 21 Oct – 20 Nov 26, from when Salary arrives", Text(cut, "account-month"));
        Assert.Equal("November 2026", Text(cut, "period-name"));
    }

    [Fact]
    public void Offers_to_start_an_accounts_month_when_its_money_arrives()
    {
        UseTransfers(true);
        var topUp = Guid.NewGuid();
        _api.DaysResponse = (from, to) => new CashFlowDaysResponse(from, to, "GBP",
        [
            new(JointId, "Joint",
            [
                new(new DateOnly(2026, 11, 1), [new(CashFlowKindDto.Outgoing, Guid.NewGuid(), "Mortgage", null, 1000m)], 0m, 1000m, -1000m, true),
                new(new DateOnly(2026, 11, 25), [new(CashFlowKindDto.TransferIn, topUp, "Bills top-up", null, 1000m)], 1000m, 0m, 0m, false),
            ], 1000m, 1000m)
            {
                Suggestion = new MonthStartSuggestion(topUp, "Bills top-up", new DateOnly(2026, 11, 25)),
            },
        ]);
        var cut = _context.RenderComponent<CashFlowPage>();
        cut.Find("[data-testid=show-daily]").Click();

        cut.WaitForAssertion(() => Assert.Contains("looks short from 1 Nov because its bills go out before Bills top-up arrives on 25 Nov", Text(cut, "month-start-tip"), StringComparison.Ordinal));
        cut.Find("[data-testid=use-month-start]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { (JointId, (Guid?)topUp) }, _api.MonthStarts));
        Assert.Equal(2, _api.Days.Count);
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

        public List<bool> AccountMonths { get; } = [];

        public List<(Guid AccountId, Guid? PaymentId)> MonthStarts { get; } = [];

        /// <summary>A day-by-day response for a test to use instead of the usual one.</summary>
        public Func<DateOnly, DateOnly, CashFlowDaysResponse>? DaysResponse { get; set; }

        public Task<bool> SetMonthStartAsync(Guid householdId, Guid accountId, Guid? paymentId)
        {
            MonthStarts.Add((accountId, paymentId));
            return Task.FromResult(true);
        }

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

        public Task<CashFlowDaysResponse> GetDaysAsync(Guid householdId, DateOnly from, DateOnly to, bool accountMonths)
        {
            Days.Add((from, to));
            AccountMonths.Add(accountMonths);
            if (DaysResponse is not null)
            {
                return Task.FromResult(DaysResponse(from, to));
            }

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
