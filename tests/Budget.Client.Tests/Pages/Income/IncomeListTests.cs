using Budget.Client.Pages.Income;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;

namespace Budget.Client.Tests.Pages.Income;

public sealed class IncomeListTests : IncomeTestContext
{
    [Fact]
    public void Income_not_paid_monthly_shows_what_is_received()
    {
        Api.Month = new IncomeMonthResponse(2026, 11,
        [
            new(Guid.NewGuid(), "Child Benefit", Guid.NewGuid(), "Chris Austin", 110.72m, 110.72m, 102.20m, 4, PaymentUnitDto.Week, [], new DateOnly(2026, 1, 5), null),
        ], 110.72m, "GBP");

        var cut = Context.RenderComponent<IncomeList>();

        Assert.Equal("£110.72", cut.Find("[data-testid=income-amount]").TextContent.Trim());
        Assert.Equal("£102.20 every 4 weeks", cut.Find("[data-testid=income-received]").TextContent.Trim());
    }

    [Fact]
    public void Shows_this_months_income_with_the_total_and_why_it_differs()
    {
        Api.Month = new IncomeMonthResponse(2026, 11,
        [
            new(Guid.NewGuid(), "Chris – salary", Guid.NewGuid(), "Chris Austin", 1735m, 3000m, 3000m, 1, PaymentUnitDto.Month,
                ["Sick leave 16 Nov 2026 – until further notice (£470.00 a month)"], new DateOnly(2026, 1, 1), null),
            new(Guid.NewGuid(), "Jane – cleaning", Guid.NewGuid(), "Jane", 850m, 850m, 850m, 1, PaymentUnitDto.Month, [], new DateOnly(2026, 1, 1), null),
        ], 2585m, "GBP");

        var cut = Context.RenderComponent<IncomeList>();

        Assert.Equal("November 2026", cut.Find("[data-testid=month-name]").TextContent.Trim());
        Assert.Equal("£2,585.00", cut.Find("[data-testid=month-total]").TextContent.Trim());
        var items = cut.FindAll("[data-testid=income-item]").ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains("Chris – salary", items[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("£1,735.00", items[0].QuerySelector("[data-testid=income-amount]")!.TextContent.Trim());
        Assert.Equal("usually £3,000.00", items[0].QuerySelector("[data-testid=income-usual]")!.TextContent.Trim());
        Assert.Contains("Sick leave", items[0].QuerySelector("[data-testid=income-note]")!.TextContent, StringComparison.Ordinal);
        Assert.Null(items[1].QuerySelector("[data-testid=income-usual]"));
        Assert.Equal((2026, 11, false), Api.MonthCalls.Single());
    }

    [Fact]
    public void The_year_view_asks_for_12_months_and_shows_no_usual_amount()
    {
        Api.Month = new IncomeMonthResponse(2026, 11,
        [
            new(Guid.NewGuid(), "Chris – salary", Guid.NewGuid(), "Chris Austin", 39450m, 3300m, 3300m, 1, PaymentUnitDto.Month, [], new DateOnly(2026, 1, 1), null),
        ], 39450m, "GBP");
        var cut = Context.RenderComponent<IncomeList>();

        cut.Find("[data-testid=show-year]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { 1, 12 }, Api.MonthsRequested));
        Assert.Equal("Nov 2026 – Oct 2027", cut.Find("[data-testid=month-name]").TextContent.Trim());
        Assert.Equal("over 12 months", cut.Find("[data-testid=income-period]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=income-usual]"));
    }

    [Fact]
    public void Moving_between_months_loads_that_month()
    {
        var cut = Context.RenderComponent<IncomeList>();

        cut.Find("[data-testid=next-month]").Click();
        cut.WaitForAssertion(() => Assert.Equal("December 2026", cut.Find("[data-testid=month-name]").TextContent.Trim()));
        cut.Find("[data-testid=previous-month]").Click();
        cut.Find("[data-testid=previous-month]").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { (2026, 11, false), (2026, 12, false), (2026, 11, false), (2026, 10, false) },
            Api.MonthCalls.ToArray()));
    }

    [Fact]
    public void An_empty_month_invites_adding_income()
    {
        var cut = Context.RenderComponent<IncomeList>();

        Assert.Contains("No income this month", cut.Find("[data-testid=income-empty]").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid=add-income]").ToList());
    }

    [Fact]
    public void Viewers_cannot_add_income()
    {
        UseRole("Viewer");

        var cut = Context.RenderComponent<IncomeList>();

        Assert.Empty(cut.FindAll("[data-testid=add-income]").ToList());
    }
}
