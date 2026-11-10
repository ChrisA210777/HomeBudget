using Budget.Client.Income;
using Budget.Client.Pages.Income;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Budget.Client.Tests.Pages.Income;

public sealed class IncomeAddTests : IncomeTestContext
{
    [Fact]
    public void Adds_income_for_you_from_the_start_of_this_month_and_opens_it()
    {
        var cut = Context.RenderComponent<IncomeAdd>();
        Assert.Empty(cut.FindAll("[data-testid=comment]")); // The first amount is recorded as "Starting amount" (BUD-78).

        cut.Find("input[data-testid=name]").Change("Chris – salary");
        cut.Find("input[data-testid=amount]").Input("3000.50");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (operation, _, body) = Assert.Single(Api.Calls);
            Assert.Equal("Create", operation);
            var request = Assert.IsType<CreateIncomeStreamRequest>(body);
            Assert.Equal("Chris – salary", request.Name);
            Assert.Equal(StaticPeopleApi.ChrisId, request.PersonId);
            Assert.Equal(3000.50m, request.Amount);
            Assert.Equal(new DateOnly(2026, 11, 1), request.StartsOn);
            Assert.Null(request.Comment);
        });
        var navigation = Context.Services.GetRequiredService<FakeNavigationManager>();
        Assert.EndsWith($"/income/{IncomeSamples.SalaryId}", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Four_weekly_income_shows_its_monthly_equivalent_and_sends_the_schedule()
    {
        var cut = Context.RenderComponent<IncomeAdd>();

        Assert.Contains("Amount received", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(IncomeValidator.AmountHelp, cut.Markup, StringComparison.Ordinal);

        cut.Find("input[data-testid=name]").Change("Child Benefit");
        cut.Find("input[data-testid=amount]").Input("102.20");
        cut.Find("input[data-testid=every]").Input("4");
        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<PaymentUnitDto>>().Instance.ValueChanged.InvokeAsync(PaymentUnitDto.Week));

        cut.WaitForAssertion(() => Assert.Equal("£102.20 every 4 weeks = £110.72 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim()));

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.IsType<CreateIncomeStreamRequest>(Assert.Single(Api.Calls).Request);
            Assert.Equal((102.20m, 4, PaymentUnitDto.Week), (request.Amount, request.Every, request.Unit));
        });
    }

    [Fact]
    public void Missing_details_are_flagged_before_anything_is_sent()
    {
        var cut = Context.RenderComponent<IncomeAdd>();

        cut.Find("[data-testid=save]").Click();

        Assert.Empty(Api.Calls);
        Assert.Contains("Enter a name", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_from_the_server_are_shown_against_the_field()
    {
        Api.NextResult = ApiResult<IncomeStreamDetails>.Invalid(new Dictionary<string, string[]>
        {
            [nameof(CreateIncomeStreamRequest.PersonId)] = ["Choose someone in this household."],
        });
        var cut = Context.RenderComponent<IncomeAdd>();

        cut.Find("input[data-testid=name]").Change("Salary");
        cut.Find("input[data-testid=amount]").Input("1000");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Choose someone in this household.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Viewers_cannot_add_income()
    {
        UseRole("Viewer");

        var cut = Context.RenderComponent<IncomeAdd>();

        Assert.NotNull(cut.Find("[data-testid=read-only]"));
        Assert.Empty(cut.FindAll("[data-testid=save]").ToList());
    }
}
