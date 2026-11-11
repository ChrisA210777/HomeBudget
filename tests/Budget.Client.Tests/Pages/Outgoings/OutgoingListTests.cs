using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.Outgoings;
using Budget.Client.Pages.Outgoings;
using Budget.Client.People;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Account;
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
    private readonly FakeUserSettingsApi _settings = new();

    public OutgoingListTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IOutgoingApi>(_api);
        _context.Services.AddSingleton<IPeopleApi>(new StaticPeopleApi());
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.Services.AddSingleton<IUserSettingsApi>(_settings);
        _context.Services.AddSingleton<Budget.Client.Accounts.IAccountsApi>(new StaticAccountsApi());
        _context.Services.AddScoped<UserSettingsStore>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    [Fact]
    public void Groups_by_category_with_subtotals_and_totals_by_priority()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal("November", cut.Find("[data-testid=month-name]").TextContent.Trim());
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
    public void Filters_change_the_list_and_the_totals()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = _context.RenderComponent<OutgoingList>();

        // BUD-131: compact menus, filled while they narrow the list.
        Assert.Equal("Kind: All", Text(cut, "filter-kind"));
        Choose(cut, popovers, "filter-kind", "Allowances");

        Assert.Equal("Kind: Allowances", Text(cut, "filter-kind"));
        Assert.Contains("mud-button-filled", cut.Find("[data-testid=filter-kind] button").ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("mud-button-filled", cut.Find("[data-testid=filter-priority] button").ClassName, StringComparison.Ordinal);
        Assert.Equal(new[] { "Food shopping" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£520.00", Text(cut, "month-total"));
        Assert.Equal("£0.00", Text(cut, "total-essential"));

        Choose(cut, popovers, "filter-kind", "All");
        Choose(cut, popovers, "filter-person", "Chris Austin");

        Assert.Equal(new[] { "Car insurance" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£40.00", Text(cut, "month-total"));

        Choose(cut, popovers, "filter-person", "Household");
        Choose(cut, popovers, "filter-priority", "Optional");

        Assert.Empty(cut.FindAll("[data-testid=outgoing-item]"));
        Assert.NotNull(cut.Find("[data-testid=outgoings-no-match]"));
        Assert.Equal("£0.00", Text(cut, "month-total"));
    }

    [Fact]
    public void Moving_between_months_and_showing_ones_not_started_or_ended_reload()
    {
        _api.Inactive = 2;
        var cut = _context.RenderComponent<OutgoingList>();

        cut.Find("[data-testid=next-month]").Click();
        cut.Find("[data-testid=previous-month]").Click();
        cut.Find("[data-testid=previous-month]").Click();
        Assert.Contains("2 outgoings haven't started yet or have ended.", cut.Find("[data-testid=inactive]").TextContent, StringComparison.Ordinal);
        cut.Find("[data-testid=show-all]").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { (2026, 11, false), (2026, 12, false), (2026, 11, false), (2026, 10, false), (2026, 10, true) },
            _api.Requests));
        Assert.Equal("October", cut.Find("[data-testid=month-name]").TextContent.Trim());
        Assert.Equal("Hide", cut.Find("[data-testid=show-all]").TextContent.Trim());
    }

    [Fact]
    public void Says_nothing_about_ones_not_started_or_ended_when_there_are_none()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Empty(cut.FindAll("[data-testid=inactive]"));
    }

    [Fact]
    public void The_year_view_asks_for_12_months_from_the_shown_month_and_says_so()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = _context.RenderComponent<OutgoingList>();
        Assert.Equal("November", Text(cut, "month-name"));
        Assert.Equal("Change period, November", cut.Find("[data-testid=month-name] button").GetAttribute("aria-label"));

        cut.ChoosePeriod(popovers, "month-name", "show-year");

        cut.WaitForAssertion(() => Assert.Equal(new[] { 1, 12 }, _api.MonthsRequested));
        Assert.Equal((2026, 11, false), _api.Requests[^1]);
        Assert.Equal("Nov 2026 – Oct 2027", Text(cut, "month-name"));
        Assert.All(Texts(cut, "outgoing-period"), text => Assert.Equal("over 12 months", text));
        Assert.Equal("Start a month later", cut.Find("[data-testid=next-month]").GetAttribute("aria-label"));

        // The arrows move the start month a month at a time.
        cut.Find("[data-testid=next-month]").Click();
        cut.WaitForAssertion(() => Assert.Equal("Dec 2026 – Nov 2027", Text(cut, "month-name")));
        Assert.Equal(((2026, 12, false), 12), (_api.Requests[^1], _api.MonthsRequested[^1]));
    }

    [Fact]
    public void On_phones_the_filters_open_in_a_panel_and_show_as_chips_while_on()
    {
        _context.RenderComponent<MudPopoverProvider>();
        var cut = _context.RenderComponent<OutgoingList>();
        Assert.Equal("Filter", Text(cut, "open-filters"));
        Assert.Empty(cut.FindAll("[data-testid=active-filters]"));

        cut.Find("[data-testid=open-filters]").Click();
        Assert.NotNull(cut.Find("[data-testid=filter-sheet]"));
        cut.InvokeAsync(() => cut.FindComponent<MudSelect<OutgoingKindDto?>>().Instance.ValueChanged.InvokeAsync(OutgoingKindDto.Allowance));

        cut.WaitForAssertion(() => Assert.Equal("Show 1 outgoing", Text(cut, "close-filters")));
        cut.Find("[data-testid=close-filters]").Click();
        Assert.Empty(cut.FindAll("[data-testid=filter-sheet]"));
        Assert.Equal("Filter (1)", Text(cut, "open-filters"));
        Assert.Equal(new[] { "Allowances" }, Texts(cut, "active-filter"));
        Assert.Equal(new[] { "Food shopping" }, Texts(cut, "outgoing-name"));

        // Removing the chip removes the filter.
        var chip = cut.FindComponents<MudChip<string>>().Single(c => c.Markup.Contains("data-testid=\"active-filter\"", StringComparison.Ordinal)).Instance;
        cut.InvokeAsync(() => chip.OnClose.InvokeAsync(chip));
        cut.WaitForAssertion(() => Assert.Equal("Filter", Text(cut, "open-filters")));
        Assert.Equal(4, Texts(cut, "outgoing-name").Length);
    }

    [Fact]
    public void Outgoings_can_be_grouped_by_the_account_they_are_paid_from()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        GroupBy(cut, paidFrom: true);

        // By name, ignoring case and spaces, with "Not set" last.
        Assert.Equal(new[] { "Chris current", "Joint bills", "Not set" }, Texts(cut, "paid-from-name"));
        Assert.Empty(cut.FindAll("[data-testid=category-group]"));
        Assert.Equal(new[] { "£40.00", "£150.00", "£520.00" }, Texts(cut, "category-subtotal"));
        Assert.Equal(new[] { "Car insurance", "Council tax", "Mortgage", "Food shopping" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£710.00", Text(cut, "month-total"));

        GroupBy(cut, paidFrom: false);
        Assert.Equal(new[] { "Housing", "Insurance", "Food & household" }, Texts(cut, "category-name"));

        // Each choice is remembered for next time (BUD-87).
        Assert.Equal(
            new OutgoingsGroupingDto?[] { OutgoingsGroupingDto.PaidFrom, OutgoingsGroupingDto.Category },
            _settings.Updates.Select(u => u.OutgoingsGrouping));
    }

    [Fact]
    public void A_group_closes_to_its_header_and_opens_again()
    {
        var cut = _context.RenderComponent<OutgoingList>();
        var housing = Group(cut, "Housing");
        Assert.Equal("true", housing.QuerySelector("[data-testid=group-header]")!.GetAttribute("aria-expanded"));

        cut.FindAll("[data-testid=group-header]").ToList()[0].Click();

        housing = Group(cut, "Housing");
        Assert.Equal("false", housing.QuerySelector("[data-testid=group-header]")!.GetAttribute("aria-expanded"));
        Assert.True(housing.QuerySelector(".mud-paper")!.HasAttribute("hidden"));
        // The header still shows the total, and the other groups and the summary are unchanged.
        Assert.Equal("£150.00", housing.QuerySelector("[data-testid=category-subtotal]")!.TextContent.Trim());
        Assert.False(Group(cut, "Insurance").QuerySelector(".mud-paper")!.HasAttribute("hidden"));
        Assert.Equal("£710.00", Text(cut, "month-total"));
        Assert.Equal("Collapse all groups", cut.Find("[data-testid=toggle-all-groups]").GetAttribute("aria-label"));

        cut.FindAll("[data-testid=group-header]").ToList()[0].Click();
        Assert.False(Group(cut, "Housing").QuerySelector(".mud-paper")!.HasAttribute("hidden"));
    }

    [Fact]
    public void Every_group_can_be_collapsed_and_expanded_at_once()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        cut.Find("[data-testid=toggle-all-groups]").Click();

        Assert.All(cut.FindAll("[data-testid=category-group]"), g => Assert.Equal("true", g.GetAttribute("data-collapsed")));
        Assert.Equal("Expand all groups", cut.Find("[data-testid=toggle-all-groups]").GetAttribute("aria-label"));

        // Still collapsed in another month; switching how they're grouped starts open again.
        cut.Find("[data-testid=next-month]").Click();
        cut.WaitForAssertion(() => Assert.All(cut.FindAll("[data-testid=category-group]"), g => Assert.Equal("true", g.GetAttribute("data-collapsed"))));
        GroupBy(cut, paidFrom: true);
        Assert.All(cut.FindAll("[data-testid=paid-from-group]"), g => Assert.Equal("false", g.GetAttribute("data-collapsed")));

        cut.Find("[data-testid=toggle-all-groups]").Click();
        cut.Find("[data-testid=toggle-all-groups]").Click();
        Assert.All(cut.FindAll("[data-testid=paid-from-group]"), g => Assert.Equal("false", g.GetAttribute("data-collapsed")));
        Assert.Equal("Collapse all groups", cut.Find("[data-testid=toggle-all-groups]").GetAttribute("aria-label"));
    }

    [Fact]
    public void Opens_in_the_view_and_grouping_chosen_last_time()
    {
        _settings.Saved = UserSettings.Defaults with { OutgoingsPeriod = ListPeriodDto.Year, OutgoingsGrouping = OutgoingsGroupingDto.PaidFrom };

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal(new[] { 12 }, _api.MonthsRequested);
        Assert.Equal("Nov 2026 – Oct 2027", Text(cut, "month-name"));
        Assert.Equal(new[] { "Chris current", "Joint bills", "Not set" }, Texts(cut, "paid-from-name"));
        Assert.Empty(_settings.Updates);
    }

    [Fact]
    public void Works_with_the_usual_view_when_settings_cant_be_loaded()
    {
        _settings.Offline = true;

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal(new[] { 1 }, _api.MonthsRequested);
        Assert.Equal(new[] { "Housing", "Insurance", "Food & household" }, Texts(cut, "category-name"));
    }

    [Fact]
    public void Outgoings_can_be_filtered_by_the_account_they_are_paid_from_including_from_a_link()
    {
        var jointBills = AccountIds.GetOrAdd("JOINT BILLS", _ => Guid.NewGuid());
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo($"/outgoings?account={jointBills}");

        var cut = _context.RenderComponent<OutgoingList>();

        // Matching outgoings only, with totals to match (BUD-95).
        Assert.Equal(new[] { "Council tax", "Mortgage" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£150.00", Text(cut, "month-total"));
        Assert.Contains("Matching", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_account_includes_its_pots_when_filtering_and_grouping()
    {
        var monzo = Guid.NewGuid();
        var holiday = Guid.NewGuid();
        _context.Services.AddSingleton<Budget.Client.Accounts.IAccountsApi>(new AccountsWithPot(monzo, holiday));
        _api.Items =
        [
            Outgoing("Food shopping", FoodId, "Food & household", 90, OutgoingPriorityDto.Preferred, 400m, PaymentUnitDto.Month, 400m) with { AccountId = monzo, PaidFrom = "Joint Monzo" },
            Outgoing("Holiday fund", FoodId, "Food & household", 90, OutgoingPriorityDto.Optional, 100m, PaymentUnitDto.Month, 100m) with { AccountId = holiday, PaidFrom = "Joint Monzo › Holiday" },
            Outgoing("Gym", FoodId, "Food & household", 90, OutgoingPriorityDto.Optional, 30m, PaymentUnitDto.Month, 30m),
        ];
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo($"/outgoings?account={monzo}");

        var cut = _context.RenderComponent<OutgoingList>();

        // Budgeting is by account; a pot just keeps money apart within it (BUD-95).
        Assert.Equal(new[] { "Food shopping", "Holiday fund" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£500.00", Text(cut, "month-total"));

        GroupBy(cut, paidFrom: true);
        Assert.Equal(new[] { "Joint Monzo" }, Texts(cut, "paid-from-name"));

        // Within the account, what's not in a pot comes first, then each pot, each with a subtotal (BUD-101).
        Assert.Equal(new[] { "£500.00" }, Texts(cut, "category-subtotal"));
        Assert.Equal(new[] { "Not in a pot", "Holiday pot" }, Texts(cut, "pot-name"));
        Assert.Equal(new[] { "£400.00", "£100.00" }, Texts(cut, "pot-subtotal"));
        Assert.DoesNotContain(cut.FindAll("[data-testid=outgoing-item]"), e => e.TextContent.Contains("Holiday pot", StringComparison.Ordinal));

        // A pot closes on its own.
        cut.FindAll("[data-testid=pot-header]").ToList()[1].Click();
        Assert.Equal(new[] { "false", "true" }, cut.FindAll("[data-testid=pot-group]").Select(e => e.GetAttribute("data-collapsed")));
        Assert.Equal("£100.00", Texts(cut, "pot-subtotal")[1]);
    }

    [Fact]
    public void An_account_without_pots_has_no_subgroups()
    {
        var cut = _context.RenderComponent<OutgoingList>();

        GroupBy(cut, paidFrom: true);

        Assert.Empty(cut.FindAll("[data-testid=pot-group]"));
    }

    private sealed class AccountsWithPot(Guid account, Guid pot) : Budget.Client.Accounts.IAccountsApi
    {
        public Task<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>> ListAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>>(
            [
                new(account, "Joint Monzo", "Monzo", null, null, null, false, "Joint Monzo", 1),
                new(pot, "Holiday", null, null, null, account, false, "Joint Monzo › Holiday", 1),
            ]);

        public Task<Budget.Client.Accounts.AccountsResult> AddAsync(Guid householdId, Budget.Shared.Accounts.SaveAccountRequest request) => throw new NotSupportedException();

        public Task<Budget.Client.Accounts.AccountsResult> UpdateAsync(Guid householdId, Guid accountId, Budget.Shared.Accounts.SaveAccountRequest request) =>
            throw new NotSupportedException();

        public Task<Budget.Client.Accounts.AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived) => throw new NotSupportedException();

        public Task<Budget.Client.Accounts.AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId) => throw new NotSupportedException();
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
    public void A_kind_can_come_from_the_link()
    {
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo("/outgoings?month=2027-01&kind=Allowance");

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal(new[] { "Food shopping" }, Texts(cut, "outgoing-name"));
        Assert.Equal("£520.00", Text(cut, "month-total"));
    }

    [Fact]
    public void Each_outgoing_links_back_to_the_list_as_shown_with_its_month_and_filters()
    {
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo("/outgoings?month=2027-01&kind=Allowance&priority=Preferred&person=household");

        var cut = _context.RenderComponent<OutgoingList>();

        Assert.Equal(new[] { "Food shopping" }, Texts(cut, "outgoing-name"));
        var href = cut.Find("[data-testid=outgoing-name]").GetAttribute("href")!;
        Assert.Equal("/outgoings?month=2027-01&kind=Allowance&priority=Preferred&person=household", Uri.UnescapeDataString(href[(href.IndexOf("from=", StringComparison.Ordinal) + 5)..]));
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

    /// <summary>Chooses the grouping from the Group menu (BUD-134).</summary>
    private static void GroupBy(IRenderedComponent<OutgoingList> cut, bool paidFrom) =>
        cut.InvokeAsync(() => cut.FindComponent<Budget.Client.Layout.FilterMenu<bool>>().Instance.ValueChanged.InvokeAsync(paidFrom)).GetAwaiter().GetResult();

    private static void Choose(IRenderedComponent<OutgoingList> cut, IRenderedComponent<MudPopoverProvider> popovers, string filter, string option)
    {
        cut.Find($"[data-testid={filter}] button").Click();
        popovers.WaitForElements($"[data-testid={filter}-option]").Single(o => o.TextContent.Trim() == option).Click();
        popovers.WaitForAssertion(() => Assert.Empty(popovers.FindAll($"[data-testid={filter}-option]")));
    }

    private static string Text(IRenderedComponent<OutgoingList> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private static string[] Texts(IRenderedComponent<OutgoingList> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => e.TextContent.Trim()).ToArray();

    private static AngleSharp.Dom.IElement Group(IRenderedComponent<OutgoingList> cut, string name) =>
        cut.FindAll("[data-testid=category-group]").Single(e => e.GetAttribute("aria-label") == name);

    private static AngleSharp.Dom.IElement Item(IRenderedComponent<OutgoingList> cut, string name) =>
        cut.FindAll("[data-testid=outgoing-item]").Single(e => e.QuerySelector("[data-testid=outgoing-name]")!.TextContent.Trim() == name);

    private void UseRole(string role) => _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, role == "Admin"));

    private static OutgoingMonthItem Outgoing(
        string name, Guid categoryId, string category, int sortOrder, OutgoingPriorityDto priority, decimal amount, PaymentUnitDto unit, decimal monthly,
        OutgoingKindDto kind = OutgoingKindDto.Bill, Guid? personId = null, string? personName = null, string[]? notes = null, bool comingUp = false, string? paidFrom = null) =>
        new(Guid.NewGuid(), name, kind, priority, categoryId, category, sortOrder, personId, personName, amount, 1, unit, monthly, monthly,
            notes ?? [], new DateOnly(2026, 1, 1), null, comingUp ? new DateOnly(2026, 11, 15) : null, comingUp, PaidFrom: paidFrom,
            AccountId: paidFrom is null ? null : AccountIds.GetOrAdd(paidFrom.Trim().ToUpperInvariant(), _ => Guid.NewGuid()));

    /// <summary>One account id per Paid From name, ignoring case and spaces, as the server would give.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> AccountIds = new();

    private sealed class FakeOutgoingApi : IOutgoingApi
    {
        public List<(int Year, int Month, bool All)> Requests { get; } = [];

        public List<int> MonthsRequested { get; } = [];

        public bool Empty { get; set; }

        /// <summary>How many the response says are left out as not started or ended (BUD-99).</summary>
        public int Inactive { get; set; }

        /// <summary>When set, returned instead of the usual outgoings.</summary>
        public IReadOnlyList<OutgoingMonthItem>? Items { get; set; }

        public Task<OutgoingMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1)
        {
            Requests.Add((year, month, all));
            MonthsRequested.Add(months);
            IReadOnlyList<OutgoingMonthItem> items = Items ?? (Empty ? [] :
            [
                Outgoing("Council tax", HousingId, "Housing", 10, OutgoingPriorityDto.Essential, 180m, PaymentUnitDto.Month, 150m, notes: ["Rises to £180.00 a month from 16 Nov 2026"], paidFrom: "Joint bills"),
                Outgoing("Mortgage", HousingId, "Housing", 10, OutgoingPriorityDto.Essential, 900m, PaymentUnitDto.Month, 0m, notes: ["Paused from 1 Nov 2026 until 1 Dec 2026"], paidFrom: " joint BILLS "),
                Outgoing("Car insurance", InsuranceId, "Insurance", 40, OutgoingPriorityDto.Essential, 480m, PaymentUnitDto.Year, 40m,
                    personId: StaticPeopleApi.ChrisId, personName: "Chris Austin", comingUp: true, paidFrom: "Chris current"),
                Outgoing("Food shopping", FoodId, "Food & household", 90, OutgoingPriorityDto.Preferred, 120m, PaymentUnitDto.Week, 520m, kind: OutgoingKindDto.Allowance),
            ]);
            return Task.FromResult(new OutgoingMonthResponse(year, month, items, "GBP", months, Inactive));
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
