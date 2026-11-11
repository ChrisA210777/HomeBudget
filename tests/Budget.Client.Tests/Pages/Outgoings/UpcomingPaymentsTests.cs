using Budget.Client.Account;
using Budget.Client.Outgoings;
using Budget.Client.Pages.Outgoings;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Account;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Outgoings;

/// <summary>The upcoming payments page (BUD-82). "Today" is Thursday 5 November 2026.</summary>
public sealed class UpcomingPaymentsTests : IDisposable
{
    private static readonly Guid HousingId = Guid.NewGuid();
    private static readonly Guid FoodId = Guid.NewGuid();
    private static readonly Guid JointId = Guid.NewGuid();
    private static readonly Guid MortgagePotId = Guid.NewGuid();
    private static readonly Guid ChrisId = Guid.NewGuid();

    private readonly TestContext _context = new();
    private readonly FakePaymentsApi _api = new();
    private readonly FakeUserSettingsApi _settings = new();

    public UpcomingPaymentsTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IUpcomingPaymentsApi>(_api);
        _context.Services.AddSingleton<IUserSettingsApi>(_settings);
        _context.Services.AddSingleton<Budget.Client.Accounts.IAccountsApi>(new FakeAccountsApi());
        _context.Services.AddScoped<UserSettingsStore>();
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Viewer", isOwner: false));
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, "2026-11-01", "2026-11-07", "1 – 7 Nov")]
    [InlineData(DayOfWeek.Monday, "2026-11-02", "2026-11-08", "2 – 8 Nov")]
    [InlineData(DayOfWeek.Thursday, "2026-11-05", "2026-11-11", "5 – 11 Nov")]
    public void Shows_this_week_starting_on_the_chosen_day(DayOfWeek firstDay, string from, string to, string name)
    {
        _settings.Saved = UserSettings.Defaults with { FirstDayOfWeek = firstDay };

        var cut = _context.RenderComponent<UpcomingPayments>();

        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), _api.Requests.Single());
        Assert.Equal(name, Text(cut, "period-name"));
        Assert.Empty(cut.FindAll("[data-testid=go-to-today]"));
    }

    [Fact]
    public void Lists_only_days_with_payments_with_day_and_period_totals()
    {
        var cut = _context.RenderComponent<UpcomingPayments>();

        Assert.Equal(new[] { "Mon 2 Nov", "Thu 5 Nov", "Fri 6 Nov" }, Texts(cut, "payment-day-name"));
        Assert.Equal(new[] { "£120.00", "£930.00", "£25.00" }, Texts(cut, "payment-day-total"));
        Assert.Equal("£1,075.00", Text(cut, "period-total"));
        Assert.Equal("4 payments", Text(cut, "payment-count"));
        Assert.Single(cut.FindAll("[data-testid=payment-today]"));
        Assert.Equal(
            new[] { "Weekly · Food & household", "Monthly · Housing · from Joint bills", "Monthly · Housing · from Joint bills › Mortgage", "Monthly · Utilities · from Chris current" },
            Texts(cut, "payment-details"));
    }

    [Fact]
    public void Moving_between_weeks_and_months_asks_for_those_days()
    {
        var cut = _context.RenderComponent<UpcomingPayments>();

        cut.Find("[data-testid=next-period]").Click();
        cut.WaitForAssertion(() => Assert.Equal("8 – 14 Nov", Text(cut, "period-name")));
        Assert.NotNull(cut.Find("[data-testid=go-to-today]"));

        cut.Find("[data-testid=show-month]").Click();
        cut.WaitForAssertion(() => Assert.Equal("November", Text(cut, "period-name")));
        cut.Find("[data-testid=next-period]").Click();
        cut.WaitForAssertion(() => Assert.Equal("December", Text(cut, "period-name")));
        cut.Find("[data-testid=next-period]").Click();
        cut.WaitForAssertion(() => Assert.Equal("January 2027", Text(cut, "period-name")));
        cut.Find("[data-testid=go-to-today]").Click();
        cut.WaitForAssertion(() => Assert.Equal("November", Text(cut, "period-name")));

        Assert.Equal(
            new[] { ("2026-11-01", "2026-11-07"), ("2026-11-08", "2026-11-14"), ("2026-11-01", "2026-11-30"), ("2026-12-01", "2026-12-31"), ("2027-01-01", "2027-01-31"), ("2026-11-01", "2026-11-30") },
            _api.Requests.Select(r => (r.From.ToString("yyyy-MM-dd"), r.To.ToString("yyyy-MM-dd"))));
    }

    [Fact]
    public void Income_arrives_on_its_days_marked_as_money_in()
    {
        _api.Income = true;

        var cut = _context.RenderComponent<UpcomingPayments>();

        Assert.Equal(new[] { "Salary" }, Texts(cut, "income-name"));
        Assert.Equal(new[] { "+£3,000.00" }, Texts(cut, "income-amount"));
        Assert.Equal(new[] { "Monthly · Income · into Chris current · Chris" }, Texts(cut, "income-details"));
        Assert.Equal("Coming in £3,000.00", Text(cut, "period-income"));
        // Going out is unchanged; the salary's day shows its net.
        Assert.Equal("£1,075.00", Text(cut, "period-total"));
        Assert.Equal(new[] { "£120.00", "£930.00", "+£2,975.00" }, Texts(cut, "payment-day-total"));
        Assert.Empty(cut.FindAll("[data-testid=account-totals]"));
    }

    [Fact]
    public void Filtering_by_account_shows_what_goes_out_of_it_and_comes_into_it_including_pots()
    {
        _api.Income = true;
        var popovers = _context.RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = _context.RenderComponent<UpcomingPayments>();

        Choose(cut, popovers, "Joint bills");

        Assert.Equal("Joint bills", Text(cut, "filter-account"));
        Assert.Equal(new[] { "Council tax", "Mortgage" }, Texts(cut, "payment-name"));
        Assert.Empty(cut.FindAll("[data-testid=income-name]"));
        Assert.Equal("£930.00", Text(cut, "period-total"));

        Choose(cut, popovers, "Chris current");

        Assert.Equal(new[] { "Phone" }, Texts(cut, "payment-name"));
        Assert.Equal(new[] { "Salary" }, Texts(cut, "income-name"));
    }

    [Fact]
    public void Says_when_nothing_is_due()
    {
        _api.Empty = true;

        var cut = _context.RenderComponent<UpcomingPayments>();

        Assert.Contains("Nothing is due this week", Text(cut, "payments-empty"), StringComparison.Ordinal);
        Assert.Equal("0 payments", Text(cut, "payment-count"));
    }

    public void Dispose() => _context.Dispose();

    /// <summary>Picks an account from the Paid from menu (BUD-124).</summary>
    private static void Choose(IRenderedComponent<UpcomingPayments> cut, IRenderedComponent<MudBlazor.MudPopoverProvider> popovers, string account)
    {
        cut.Find("[data-testid=filter-account] button").Click();
        popovers.WaitForElements("[data-testid=filter-account-option]").Single(o => o.TextContent.Trim() == account).Click();

        // The menu closes once a choice is made, ready to open again.
        popovers.WaitForAssertion(() => Assert.Empty(popovers.FindAll("[data-testid=filter-account-option]")));
        cut.WaitForAssertion(() => Assert.Equal(account, cut.Find("[data-testid=filter-account]").TextContent.Trim()));
    }

    private static string Text(IRenderedComponent<UpcomingPayments> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private static string[] Texts(IRenderedComponent<UpcomingPayments> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => string.Join(' ', e.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray();

    private sealed class FakePaymentsApi : IUpcomingPaymentsApi
    {
        public List<(DateOnly From, DateOnly To)> Requests { get; } = [];

        public bool Empty { get; set; }

        public bool Income { get; set; }

        public Task<UpcomingPaymentsResponse> GetAsync(Guid householdId, DateOnly from, DateOnly to)
        {
            Requests.Add((from, to));
            IReadOnlyList<UpcomingPayment> payments = Empty ? [] :
            [
                Payment(new DateOnly(2026, 11, 2), "Food shopping", 120m, PaymentUnitDto.Week, FoodId, "Food & household", 90, null, null),
                Payment(new DateOnly(2026, 11, 5), "Council tax", 30m, PaymentUnitDto.Month, HousingId, "Housing", 10, "Joint bills", JointId),
                Payment(new DateOnly(2026, 11, 5), "Mortgage", 900m, PaymentUnitDto.Month, HousingId, "Housing", 10, "Joint bills › Mortgage", MortgagePotId),
                Payment(new DateOnly(2026, 11, 6), "Phone", 25m, PaymentUnitDto.Month, Guid.NewGuid(), "Utilities", 20, "Chris current", ChrisId),
            ];
            IReadOnlyList<UpcomingIncome> income = Income
                ? [new UpcomingIncome(new DateOnly(2026, 11, 6), Guid.NewGuid(), "Salary", 3000m, 1, PaymentUnitDto.Month, "Chris current", "Chris", ChrisId)]
                : [];
            return Task.FromResult(new UpcomingPaymentsResponse(from, to, "GBP", payments, income));
        }

        private static UpcomingPayment Payment(
            DateOnly date, string name, decimal amount, PaymentUnitDto unit, Guid categoryId, string category, int sortOrder, string? paidFrom, Guid? accountId) =>
            new(date, Guid.NewGuid(), name, amount, 1, unit, OutgoingKindDto.Bill, OutgoingPriorityDto.Essential, categoryId, category, sortOrder, "home", paidFrom, null, accountId);
    }

    private sealed class FakeAccountsApi : Budget.Client.Accounts.IAccountsApi
    {
        public Task<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>> ListAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>>(
            [
                new(ChrisId, "Chris current", null, null, null, null, false, "Chris current", 1),
                new(JointId, "Joint bills", null, null, null, null, false, "Joint bills", 1),
                new(MortgagePotId, "Mortgage", null, null, null, JointId, false, "Joint bills › Mortgage", 1),
            ]);

        public Task<Budget.Client.Accounts.AccountsResult> AddAsync(Guid householdId, Budget.Shared.Accounts.SaveAccountRequest request) => throw new NotSupportedException();

        public Task<Budget.Client.Accounts.AccountsResult> UpdateAsync(Guid householdId, Guid accountId, Budget.Shared.Accounts.SaveAccountRequest request) =>
            throw new NotSupportedException();

        public Task<Budget.Client.Accounts.AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived) => throw new NotSupportedException();

        public Task<Budget.Client.Accounts.AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId) => throw new NotSupportedException();
    }
}
