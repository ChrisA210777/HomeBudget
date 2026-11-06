using Budget.Client.Income;
using Budget.Client.Pages.Income;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Income;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Client.Tests.Pages.Income;

public sealed class IncomeDetailTests : IncomeTestContext
{
    public IncomeDetailTests() => Api.Details = IncomeSamples.Salary();

    [Fact]
    public void Shows_the_full_history_newest_first()
    {
        var cut = Render();

        Assert.Equal("Chris – salary", cut.Find("[data-testid=income-name]").TextContent.Trim());
        Assert.Equal("£3,000.00 a month", cut.Find("[data-testid=amount-received]").TextContent.Trim());
        Assert.Equal("£3,000.00 a month today", cut.Find("[data-testid=amount-today]").TextContent.Trim());
        var changes = cut.FindAll("[data-testid=amount-change]").ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal("£3,300.00 a month", changes[0].QuerySelector("[data-testid=entry-amount]")!.TextContent.Trim());
        Assert.NotNull(changes[0].QuerySelector("[data-testid=upcoming]"));
        Assert.Null(changes[1].QuerySelector("[data-testid=delete-entry]"));
        var period = Assert.Single(cut.FindAll("[data-testid=status-period]").ToList());
        Assert.Contains("Sick leave", period.TextContent, StringComparison.Ordinal);
        Assert.Contains("until further notice", period.TextContent, StringComparison.Ordinal);
        Assert.Contains("Paid £470.00 a month", period.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_the_amount_needs_a_comment_and_sends_the_date_and_version()
    {
        var cut = Render();
        cut.Find("[data-testid=change-amount]").Click();
        cut.Find("input[data-testid=amount]").Input("3150");
        cut.Find("[data-testid=save]").Click();

        Assert.Empty(Api.Calls);
        Assert.Contains("Say why the amount is changing", cut.Markup, StringComparison.Ordinal);

        cut.Find("input[data-testid=comment]").Change("5% pay rise");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (operation, _, body) = Assert.Single(Api.Calls);
            Assert.Equal("AddAmount", operation);
            var request = Assert.IsType<SaveAmountChangeRequest>(body);
            Assert.Equal(3150m, request.Amount);
            Assert.Equal(new DateOnly(2026, 11, 5), request.EffectiveFrom);
            Assert.Equal("5% pay rise", request.Comment);
            Assert.Null(request.CorrectionReason);
            Assert.Equal("AAAAAAAAAAE=", request.Version);
            Assert.Empty(cut.FindAll("[data-testid=editor]").ToList());
        });
    }

    [Fact]
    public void Correcting_an_entry_starts_from_it_and_needs_a_reason()
    {
        var cut = Render();
        cut.FindAll("[data-testid=amount-change] [data-testid=correct-entry]").ToList()[0].Click();

        Assert.Equal("3300", cut.Find("input[data-testid=amount]").GetAttribute("value"));
        cut.Find("[data-testid=save]").Click();
        Assert.Empty(Api.Calls);
        Assert.Contains("correcting this.", cut.Markup, StringComparison.Ordinal);

        cut.Find("input[data-testid=correction-reason]").Change("Typo – it was 10%");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (operation, entryId, body) = Assert.Single(Api.Calls);
            Assert.Equal("CorrectAmount", operation);
            Assert.Equal(IncomeSamples.RiseId, entryId);
            var request = Assert.IsType<SaveAmountChangeRequest>(body);
            Assert.Equal(new DateOnly(2026, 12, 1), request.EffectiveFrom);
            Assert.Equal("Typo – it was 10%", request.CorrectionReason);
        });
    }

    [Fact]
    public void Deleting_leave_needs_a_reason()
    {
        var cut = Render();
        cut.Find("[data-testid=status-period] [data-testid=delete-entry]").Click();

        Assert.Contains("Sick leave", cut.Find("[data-testid=delete-summary]").TextContent, StringComparison.Ordinal);
        cut.Find("input[data-testid=correction-reason]").Change("Added to the wrong income");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (operation, entryId, body) = Assert.Single(Api.Calls);
            Assert.Equal("DeletePeriod", operation);
            Assert.Equal(IncomeSamples.LeaveId, entryId);
            Assert.Equal("Added to the wrong income", Assert.IsType<DeleteEntryRequest>(body).Reason);
        });
    }

    [Fact]
    public void Ending_the_income_records_an_ended_period_from_today()
    {
        var cut = Render();
        cut.Find("[data-testid=end-income]").Click();
        cut.Find("input[data-testid=comment]").Change("Left the job");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (operation, _, body) = Assert.Single(Api.Calls);
            Assert.Equal("AddPeriod", operation);
            var request = Assert.IsType<SaveStatusPeriodRequest>(body);
            Assert.Equal(IncomeStatusReasonDto.Ended, request.Reason);
            Assert.Equal(new DateOnly(2026, 11, 5), request.StartDate);
            Assert.Null(request.EndDate);
            Assert.Null(request.ReducedMonthlyAmount);
        });
    }

    [Fact]
    public void Adding_unpaid_leave_sends_no_amount()
    {
        var cut = Render();
        cut.Find("[data-testid=add-period]").Click();
        cut.Find("input[data-testid=comment]").Change("Two weeks travelling");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.IsType<SaveStatusPeriodRequest>(Assert.Single(Api.Calls).Request);
            Assert.Equal(IncomeStatusReasonDto.UnpaidLeave, request.Reason);
            Assert.Null(request.ReducedMonthlyAmount);
            Assert.Equal("Two weeks travelling", request.Comment);
        });
    }

    [Fact]
    public void A_conflict_reloads_the_income_and_says_so()
    {
        Api.NextResult = new ApiResult<IncomeStreamDetails>(null, new Dictionary<string, string[]>(), Conflict: true);
        var cut = Render();
        cut.Find("[data-testid=change-amount]").Click();
        cut.Find("input[data-testid=amount]").Input("3150");
        cut.Find("input[data-testid=comment]").Change("Pay rise");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=conflict]"));
            Assert.Equal(2, Api.GetCalls);
        });
    }

    [Fact]
    public void Server_rule_errors_are_shown_against_the_field()
    {
        Api.NextResult = ApiResult<IncomeStreamDetails>.Invalid(new Dictionary<string, string[]>
        {
            [nameof(SaveAmountChangeRequest.EffectiveFrom)] = ["There's already an amount change on that date."],
        });
        var cut = Render();
        cut.Find("[data-testid=change-amount]").Click();
        cut.Find("input[data-testid=amount]").Input("3150");
        cut.Find("input[data-testid=comment]").Change("Pay rise");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Contains("already an amount change on that date", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Deleting_the_income_goes_back_to_the_list()
    {
        var cut = Render();
        cut.Find("[data-testid=delete-income]").Click();
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Equal(IncomeSamples.SalaryId, Assert.Single(Api.Deleted)));
        Assert.EndsWith("/income", Context.Services.GetRequiredService<FakeNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewers_see_the_history_but_cannot_change_it()
    {
        UseRole("Viewer");

        var cut = Render();

        Assert.Equal(2, cut.FindAll("[data-testid=amount-change]").ToList().Count);
        Assert.Empty(cut.FindAll("[data-testid=change-amount]").ToList());
        Assert.Empty(cut.FindAll("[data-testid=correct-entry]").ToList());
        Assert.Empty(cut.FindAll("[data-testid=delete-entry]").ToList());
    }

    [Fact]
    public void A_missing_income_says_so()
    {
        Api.Details = null;

        var cut = Render();

        Assert.NotNull(cut.Find("[data-testid=not-found]"));
    }

    private IRenderedComponent<IncomeDetail> Render() =>
        Context.RenderComponent<IncomeDetail>(p => p.Add(c => c.IncomeStreamId, IncomeSamples.SalaryId));
}
