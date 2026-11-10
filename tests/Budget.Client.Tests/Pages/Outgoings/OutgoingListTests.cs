using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.Outgoings;
using Budget.Client.Pages.Outgoings;
using Budget.Client.People;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Outgoings;

public sealed class OutgoingListTests : IDisposable
{
    private static readonly Guid HousingId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid InsuranceId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid FoodId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly TestContext _context = new();
    private readonly FakeOutgoingApi _api = new();

    public OutgoingListTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IOutgoingApi>(_api);
        _context.Services.AddSingleton<IPeopleApi>(new StaticPeopleApi());
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    [Fact]
    public void Groups_by_category_with_subtotals_and_totals_by_priority()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal("November 2026", cut.Find("[data-testid=month-name]").TextContent.Trim());
        Assert.Equal(new[] { "Housing", "Insurance", "Food & household" }, Texts(cut, "category-name"));
        Assert.Equal(3, cut.FindAll("[data-testid=category-group] [data-testid=category-icon]").Count);
        Assert.Equal(new[] { "£150.00", "£40.00", "£520.00" }, Texts(cut, "category-subtotal"));
        Assert.Equal("£710.00", Text(cut, "month-total"));
        Assert.Equal("£190.00", Text(cut, "total-essential"));
        Assert.Equal("£520.00", Text(cut, "total-preferred"));
        Assert.Equal("£0.00", Text(cut, "total-optional"));
    }

    [Fact]
    public void Each_outgoing_shows_what_is_paid_its_monthly_amount_and_notes()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        var insurance = Item(cut, "Car insurance");
        Assert.Equal("£480.00 a year", insurance.QuerySelector("[data-testid=outgoing-paid]")!.TextContent.Trim());
        Assert.Equal("£40.00", insurance.QuerySelector("[data-testid=outgoing-monthly]")!.TextContent.Trim());
        Assert.Contains("Chris Austin", insurance.TextContent, StringComparison.Ordinal);
        Assert.Contains("Essential", insurance.QuerySelector("[data-testid=outgoing-priority]")!.TextContent, StringComparison.Ordinal);

        var mortgage = Item(cut, "Mortgage");
        Assert.Equal("Paused from 1 Nov 2026 until 1 Dec 2026", mortgage.QuerySelector("[data-testid=outgoing-note]")!.TextContent.Trim());
    }

    [Fact]
    public void Outgoings_coming_up_for_renewal_are_highlighted()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        var insurance = Item(cut, "Car insurance");
        Assert.Contains("outgoing-coming-up", insurance.ClassName, StringComparison.Ordinal);
        var badge = insurance.QuerySelector("[data-testid=outgoing-coming-up]")!;
        Assert.Contains("Renews in 10 days", badge.TextContent, StringComparison.Ordinal);
        Assert.Contains("needs renewing by 15 Nov 2026", badge.GetAttribute("title"), StringComparison.Ordinal);
        Assert.DoesNotContain("outgoing-coming-up", Item(cut, "Mortgage").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filters_change_the_list_and_the_totals()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<OutgoingKindDto?>>().Instance.ValueChanged.InvokeAsync(OutgoingKindDto.Allowance));

        Assert.Equal(new[] { "Food shopping" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£520.00", Text(cut, "month-total"));
        Assert.Equal("£0.00", Text(cut, "total-essential"));

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<OutgoingKindDto?>>().Instance.ValueChanged.InvokeAsync(null));
        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync(StaticPeopleApi.ChrisId.ToString()));

        Assert.Equal(new[] { "Car insurance" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£40.00", Text(cut, "month-total"));

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("household"));
        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<OutgoingPriorityDto?>>().Instance.ValueChanged.InvokeAsync(OutgoingPriorityDto.Optional));

        Assert.Empty(cut.FindAll("[data-testid=outgoing-item]"));
        Assert.NotNull(cut.Find("[data-testid=outgoings-no-match]"));
        Assert.Equal("£0.00", Text(cut, "month-total"));
    }

    [Fact]
    public async Task Moving_between_months_and_show_all_reload()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        cut.Find("[data-testid=next-month]").Click();
        cut.Find("[data-testid=previous-month]").Click();
        cut.Find("[data-testid=previous-month]").Click();
        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { (2026, 11, false), (2026, 12, false), (2026, 11, false), (2026, 10, false), (2026, 10, true) },
            _api.Requests));
        Assert.Equal("October 2026", cut.Find("[data-testid=month-name]").TextContent.Trim());
    }

    [Fact]
    public void The_year_view_asks_for_12_months_from_the_shown_month_and_says_so()
    {
        var cut = _context.RenderComponent<OutgoingList>();
        Assert.Equal("true", cut.Find("[data-testid=show-month]").GetAttribute("aria-pressed"));

        cut.Find("[data-testid=show-year]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { 1, 12 }, _api.MonthsRequested));
        Assert.Equal((2026, 11, false), _api.Requests[^1]);
        Assert.Equal("true", cut.Find("[data-testid=show-year]").GetAttribute("aria-pressed"));
        Assert.Equal("Nov 2026 – Oct 2027", Text(cut, "month-name"));
        Assert.All(Texts(cut, "outgoing-period"), text => Assert.Equal("over 12 months", text));
        Assert.Equal("Start a month later", cut.Find("[data-testid=next-month]").GetAttribute("aria-label"));

        // The arrows move the start month a month at a time.
        cut.Find("[data-testid=next-month]").Click();
        cut.WaitForAssertion(() => Assert.Equal("Dec 2026 – Nov 2027", Text(cut, "month-name")));
        Assert.Equal(((2026, 12, false), 12), (_api.Requests[^1], _api.MonthsRequested[^1]));
    }

    [Fact]
    public void Outgoings_can_be_grouped_by_the_account_they_are_paid_from()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        cut.Find("[data-testid=group-paid-from]").Click();

        // By name, ignoring case and spaces, with "Not set" last.
        Assert.Equal(new[] { "Chris current", "Joint bills", "Not set" }, Texts(cut, "paid-from-name"));
        Assert.Empty(cut.FindAll("[data-testid=category-group]"));
        Assert.Equal(new[] { "£40.00", "£150.00", "£520.00" }, Texts(cut, "category-subtotal"));
        Assert.Equal(new[] { "Car insurance", "Council tax", "Mortgage", "Food shopping" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£710.00", Text(cut, "month-total"));

        cut.Find("[data-testid=group-category]").Click();
        Assert.Equal(new[] { "Housing", "Insurance", "Food & household" }, Texts(cut, "category-name"));
    }

    [Fact]
    public void A_month_and_category_can_come_from_the_link()
    {
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo($"/outgoings?month=2027-01&category={HousingId}");

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal((2027, 1, false), _api.Requests.Single());
        Assert.Equal(new[] { "Council tax", "Mortgage" }, Texts(cut, "outgoing-name"));
        Assert.Contains("Housing", cut.Find("[data-testid=category-filter]").TextContent, StringComparison.Ordinal);
        Assert.Equal("£150.00", Text(cut, "month-total"));

        cut.Find("[data-testid=clear-category]").Click();

        Assert.Equal(4, cut.FindAll("[data-testid=outgoing-item]").Count);
    }

    [Fact]
    public void An_empty_month_explains_how_to_add_one()
    {
        _api.Empty = true;

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Contains("Add your bills", cut.Find("[data-testid=outgoings-empty]").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=add-outgoing]"));
    }

    [Fact]
    public void Viewers_can_look_but_not_add()
    {
        UseRole("Viewer");
        _api.Empty = true;

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Empty(cut.FindAll("[data-testid=add-outgoing]"));
        Assert.Contains("Nobody has added", cut.Find("[data-testid=outgoings-empty]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Add an outgoing", cut.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _context.Dispose();

    private static string Text(IRenderedComponent<OutgoingList> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private static string[] Texts(IRenderedComponent<OutgoingList> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => e.TextContent.Trim()).ToArray();

    private static AngleSharp.Dom.IElement Item(IRenderedComponent<OutgoingList> cut, string name) =>
        cut.FindAll("[data-testid=outgoing-item]").Single(e => e.QuerySelector("[data-testid=outgoing-name]")!.TextContent.Trim() == name);

    private void UseRole(string role) => _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, role == "Admin"));

    private static OutgoingMonthItem Outgoing(
        string name, Guid categoryId, string category, int sortOrder, OutgoingPriorityDto priority, decimal amount, PaymentUnitDto unit, decimal monthly,
        OutgoingKindDto kind = OutgoingKindDto.Bill, Guid? personId = null, string? personName = null, string[]? notes = null, bool comingUp = false, string? paidFrom = null) =>
        new(Guid.NewGuid(), name, kind, priority, categoryId, category, sortOrder, personId, personName, amount, 1, unit, monthly, monthly,
            notes ?? [], new DateOnly(2026, 1, 1), null, comingUp ? new DateOnly(2026, 11, 15) : null, comingUp, PaidFrom: paidFrom);

    private sealed class FakeOutgoingApi : IOutgoingApi
    {
        public List<(int Year, int Month, bool All)> Requests { get; } = [];

        public List<int> MonthsRequested { get; } = [];

        public bool Empty { get; set; }

        public Task<OutgoingMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1)
        {
            Requests.Add((year, month, all));
            MonthsRequested.Add(months);
            IReadOnlyList<OutgoingMonthItem> items = Empty ? [] :
            [
                Outgoing("Council tax", HousingId, "Housing", 10, OutgoingPriorityDto.Essential, 180m, PaymentUnitDto.Month, 150m, notes: ["Rises to £180.00 a month from 16 Nov 2026"], paidFrom: "Joint bills"),
                Outgoing("Mortgage", HousingId, "Housing", 10, OutgoingPriorityDto.Essential, 900m, PaymentUnitDto.Month, 0m, notes: ["Paused from 1 Nov 2026 until 1 Dec 2026"], paidFrom: " joint BILLS "),
                Outgoing("Car insurance", InsuranceId, "Insurance", 40, OutgoingPriorityDto.Essential, 480m, PaymentUnitDto.Year, 40m,
                    personId: StaticPeopleApi.ChrisId, personName: "Chris Austin", comingUp: true, paidFrom: "Chris current"),
                Outgoing("Food shopping", FoodId, "Food & household", 90, OutgoingPriorityDto.Preferred, 120m, PaymentUnitDto.Week, 520m, kind: OutgoingKindDto.Allowance),
            ];
            return Task.FromResult(new OutgoingMonthResponse(year, month, items, "GBP", months));
        }

        public Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ReferenceLabelsAsync(Guid householdId) => Task.FromResult<IReadOnlyList<string>>(["Policy number"]);

        public Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<OutgoingDetailsResponse?> GetAsync(Guid householdId, Guid outgoingId) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> UpdateAsync(Guid householdId, Guid outgoingId, UpdateOutgoingRequest request) => throw new NotSupportedException();

        public Task DeleteAsync(Guid householdId, Guid outgoingId) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> AddAmountChangeAsync(Guid householdId, Guid outgoingId, SaveOutgoingAmountRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> CorrectAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingAmountRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> DeleteAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> AddStatusPeriodAsync(Guid householdId, Guid outgoingId, SaveOutgoingStatusRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> CorrectStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingStatusRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> DeleteStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) => throw new NotSupportedException();
    }
}
