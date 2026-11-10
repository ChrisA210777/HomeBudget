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

    private readonly TestContext _context = new();
    private readonly FakePaymentsApi _api = new();
    private readonly FakeUserSettingsApi _settings = new();

    public UpcomingPaymentsTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IUpcomingPaymentsApi>(_api);
        _context.Services.AddSingleton<IUserSettingsApi>(_settings);
        _context.Services.AddScoped<UserSettingsStore>();
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Viewer", isOwner: false));
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, "2026-11-01", "2026-11-07", "1 Nov – 7 Nov 2026")]
    [InlineData(DayOfWeek.Monday, "2026-11-02", "2026-11-08", "2 Nov – 8 Nov 2026")]
    [InlineData(DayOfWeek.Thursday, "2026-11-05", "2026-11-11", "5 Nov – 11 Nov 2026")]
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

        Assert.Equal(new[] { "Mon 2 Nov", "Thu 5 Nov" }, Texts(cut, "payment-day-name"));
        Assert.Equal(new[] { "£120.00", "£930.00" }, Texts(cut, "payment-day-total"));
        Assert.Equal("£1,050.00", Text(cut, "period-total"));
        Assert.Equal("3 payments", Text(cut, "payment-count"));
        Assert.Single(cut.FindAll("[data-testid=payment-today]"));
        Assert.Equal(new[] { "Weekly · Food & household", "Monthly · Housing · from Joint bills", "Monthly · Housing · from joint BILLS" }, Texts(cut, "payment-details"));
    }

    [Fact]
    public void Moving_between_weeks_and_months_asks_for_those_days()
    {
        var cut = _context.RenderComponent<UpcomingPayments>();

        cut.Find("[data-testid=next-period]").Click();
        cut.WaitForAssertion(() => Assert.Equal("8 Nov – 14 Nov 2026", Text(cut, "period-name")));
        Assert.NotNull(cut.Find("[data-testid=go-to-today]"));

        cut.Find("[data-testid=show-month]").Click();
        cut.WaitForAssertion(() => Assert.Equal("November 2026", Text(cut, "period-name")));
        cut.Find("[data-testid=next-period]").Click();
        cut.WaitForAssertion(() => Assert.Equal("December 2026", Text(cut, "period-name")));
        cut.Find("[data-testid=go-to-today]").Click();
        cut.WaitForAssertion(() => Assert.Equal("November 2026", Text(cut, "period-name")));

        Assert.Equal(
            new[] { ("2026-11-01", "2026-11-07"), ("2026-11-08", "2026-11-14"), ("2026-11-01", "2026-11-30"), ("2026-12-01", "2026-12-31"), ("2026-11-01", "2026-11-30") },
            _api.Requests.Select(r => (r.From.ToString("yyyy-MM-dd"), r.To.ToString("yyyy-MM-dd"))));
    }

    [Fact]
    public void Payments_can_be_grouped_by_the_account_they_are_paid_from()
    {
        var cut = _context.RenderComponent<UpcomingPayments>();

        cut.Find("[data-testid=group-paid-from]").Click();

        Assert.Equal(new[] { "Joint bills", "Not set" }, Texts(cut, "payment-group-name"));
        Assert.Equal(new[] { "£930.00", "£120.00" }, Texts(cut, "payment-group-total"));
        Assert.Equal(new[] { "Council tax", "Mortgage", "Food shopping" }, Texts(cut, "payment-name"));
        Assert.Equal("£1,050.00", Text(cut, "period-total"));
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

    private static string Text(IRenderedComponent<UpcomingPayments> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private static string[] Texts(IRenderedComponent<UpcomingPayments> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => e.TextContent.Trim()).ToArray();

    private sealed class FakePaymentsApi : IUpcomingPaymentsApi
    {
        public List<(DateOnly From, DateOnly To)> Requests { get; } = [];

        public bool Empty { get; set; }

        public Task<UpcomingPaymentsResponse> GetAsync(Guid householdId, DateOnly from, DateOnly to)
        {
            Requests.Add((from, to));
            IReadOnlyList<UpcomingPayment> payments = Empty ? [] :
            [
                Payment(new DateOnly(2026, 11, 2), "Food shopping", 120m, PaymentUnitDto.Week, FoodId, "Food & household", 90, null),
                Payment(new DateOnly(2026, 11, 5), "Council tax", 30m, PaymentUnitDto.Month, HousingId, "Housing", 10, "Joint bills"),
                Payment(new DateOnly(2026, 11, 5), "Mortgage", 900m, PaymentUnitDto.Month, HousingId, "Housing", 10, " joint BILLS "),
            ];
            return Task.FromResult(new UpcomingPaymentsResponse(from, to, "GBP", payments));
        }

        private static UpcomingPayment Payment(
            DateOnly date, string name, decimal amount, PaymentUnitDto unit, Guid categoryId, string category, int sortOrder, string? paidFrom) =>
            new(date, Guid.NewGuid(), name, amount, 1, unit, OutgoingKindDto.Bill, OutgoingPriorityDto.Essential, categoryId, category, sortOrder, "home", paidFrom, null);
    }
}
