using Budget.Client.Account;
using Budget.Client.Budgeting;
using Budget.Client.Pages.Budgeting;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Budgeting;

public sealed class BudgetPageTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakeBudgetApi _api = new();

    public BudgetPageTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IBudgetApi>(_api);
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Viewer", isOwner: false));
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Shows_income_outgoings_by_priority_and_whats_left_after_each()
    {
        var cut = _context.RenderComponent<BudgetPage>();

        Assert.Equal("November 2026", Text(cut, "month-name"));
        Assert.Equal("£3,000.00", Text(cut, "budget-income"));
        Assert.Equal("− £1,090.00", Text(cut, "budget-essential"));
        Assert.Equal("£1,910.00", Text(cut, "left-essential"));
        Assert.Equal("− £520.00", Text(cut, "budget-preferred"));
        Assert.Equal("£1,390.00", Text(cut, "left-preferred"));
        Assert.Equal("− £100.00", Text(cut, "budget-optional"));
        Assert.Equal("£1,290.00", Text(cut, "left-everything"));
        Assert.Empty(cut.FindAll("[data-testid=shortfall-warning]"));
    }

    [Fact]
    public void A_shortfall_is_shown_in_words_as_well_as_colour()
    {
        _api.Month = FakeBudgetApi.Sample(2026, 11, income: 1500m, essential: 1200m, preferred: 400m, optional: 50m);

        var cut = _context.RenderComponent<BudgetPage>();

        Assert.Equal("£300.00", Text(cut, "left-essential"));
        Assert.Equal("Short by £100.00", Text(cut, "left-preferred"));
        Assert.Equal("Short by £150.00", Text(cut, "left-everything"));
        Assert.Contains("£150.00 more than income", Text(cut, "shortfall-warning"), StringComparison.Ordinal);
        Assert.Contains("essentials are covered", Text(cut, "shortfall-warning"), StringComparison.Ordinal);
    }

    [Fact]
    public void Breaks_down_by_kind_and_by_category_with_links_to_the_outgoings()
    {
        var cut = _context.RenderComponent<BudgetPage>();

        Assert.Equal(new[] { "£1,090.00", "£100.00", "£520.00" }, cut.FindAll("[data-testid=kind-amount]").Select(e => e.TextContent.Trim()));
        var links = cut.FindAll("[data-testid=category-link]").ToList();
        Assert.Equal(new[] { "Housing", "Food & household", "Savings & set-asides" }, links.Select(e => e.TextContent.Trim()));
        Assert.Equal("/outgoings?month=2026-11&category=eeeeeeee-0000-0000-0000-000000000001", links[0].GetAttribute("href"));
    }

    [Fact]
    public void Moving_between_months_loads_that_month()
    {
        var cut = _context.RenderComponent<BudgetPage>();

        cut.Find("[data-testid=next-month]").Click();
        cut.Find("[data-testid=next-month]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new (int?, int?)[] { (2026, 11), (2026, 12), (2027, 1) }, _api.Requests));
        Assert.Equal("January 2027", Text(cut, "month-name"));
        Assert.Equal("/outgoings?month=2027-01&category=eeeeeeee-0000-0000-0000-000000000001", cut.FindAll("[data-testid=category-link]").ToList()[0].GetAttribute("href"));
    }

    [Fact]
    public void An_empty_month_says_where_to_start()
    {
        _api.Month = FakeBudgetApi.Sample(2026, 11, 0m, 0m, 0m, 0m) with { ByCategory = [] };

        var cut = _context.RenderComponent<BudgetPage>();

        Assert.NotNull(cut.Find("[data-testid=budget-empty]"));
    }

    public void Dispose() => _context.Dispose();

    private static string Text(IRenderedComponent<BudgetPage> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();
}
