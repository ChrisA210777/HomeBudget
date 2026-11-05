using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.Pages.Income;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Income;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Income;

public sealed class IncomeListTests : IDisposable
{
    private static readonly IncomeStreamSummary Salary = new(
        Guid.NewGuid(), IncomeStreamTypeDto.Employment, "Sam – Acme Ltd", "Acme Ltd", FakeAccountApi.UserId, "Chris Austin",
        PayFrequencyDto.FourWeekly, IncomeKindDto.Fixed, IsActive: true,
        2307.69m, 2307.69m, 2307.69m, 30000m, 30000m, 30000m);

    private static readonly IncomeStreamSummary Overtime = new(
        Guid.NewGuid(), IncomeStreamTypeDto.Employment, "Overtime", null, null, null,
        PayFrequencyDto.Weekly, IncomeKindDto.Variable, IsActive: true,
        300m, 450m, 700m, 15600m, 23400m, 36400m);

    private readonly TestContext _context = new();
    private readonly FakeIncomeApi _incomeApi = new();

    public IncomeListTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IIncomeApi>(_incomeApi);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    [Fact]
    public void Shows_totals_and_each_stream_with_per_pay_and_annual_figures()
    {
        _incomeApi.List = new IncomeStreamListResponse([Overtime, Salary], 53400m, 4450m, "GBP");

        var cut = _context.RenderComponent<IncomeList>();

        Assert.Equal("£4,450.00", cut.Find("[data-testid=total-monthly]").TextContent.Trim());
        Assert.Equal("£53,400.00", cut.Find("[data-testid=total-annual]").TextContent.Trim());

        // ToList: indexing bUnit's element collection directly fails with the pinned AngleSharp 1.5.
        var items = cut.FindAll("[data-testid=income-item]").ToList();
        Assert.Equal(2, items.Count);

        var salary = items[1];
        Assert.Equal("Sam – Acme Ltd", salary.QuerySelector("[data-testid=income-name]")!.TextContent.Trim());
        Assert.Equal("£2,307.69", salary.QuerySelector("[data-testid=income-per-pay]")!.TextContent.Trim());
        Assert.Equal("£30,000.00", salary.QuerySelector("[data-testid=income-annual]")!.TextContent.Trim());
        Assert.Contains("Chris Austin · Acme Ltd · 4-weekly, fixed pay", salary.TextContent, StringComparison.Ordinal);
        Assert.Null(salary.QuerySelector("[data-testid=income-range]"));

        var overtime = items[0];
        Assert.Equal("£450.00", overtime.QuerySelector("[data-testid=income-per-pay]")!.TextContent.Trim());
        Assert.Equal("£300.00 – £700.00", overtime.QuerySelector("[data-testid=income-range]")!.TextContent.Trim());
        Assert.Contains("average per weekly pay", overtime.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Explains_what_to_do_when_there_is_no_income()
    {
        var cut = _context.RenderComponent<IncomeList>();

        Assert.Contains("Add each job", cut.Find("[data-testid=income-empty]").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=add-income]"));
    }

    [Fact]
    public void Viewers_cannot_add_or_change_income()
    {
        UseRole("Viewer", isOwner: false);
        _incomeApi.List = new IncomeStreamListResponse([Salary], 30000m, 2500m, "GBP");

        var cut = _context.RenderComponent<IncomeList>();

        Assert.Empty(cut.FindAll("[data-testid=add-income]"));
        Assert.Empty(cut.FindAll("[data-testid=income-actions]"));
        Assert.Single(cut.FindAll("[data-testid=income-item]"));
    }

    [Fact]
    public void Ended_income_is_only_requested_when_asked_for()
    {
        var cut = _context.RenderComponent<IncomeList>();
        Assert.Equal(new[] { false }, _incomeApi.ListCalls);

        cut.Find("input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(new[] { false, true }, _incomeApi.ListCalls));
    }

    [Fact]
    public void Ended_income_is_labelled()
    {
        _incomeApi.List = new IncomeStreamListResponse([Salary with { IsActive = false }], 0m, 0m, "GBP");

        var cut = _context.RenderComponent<IncomeList>();

        Assert.Equal("Ended", cut.Find("[data-testid=income-ended]").TextContent.Trim());
    }

    [Fact]
    public void Marking_income_as_ended_tells_the_server_and_reloads()
    {
        _incomeApi.List = new IncomeStreamListResponse([Salary], 30000m, 2500m, "GBP");
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = _context.RenderComponent<IncomeList>();

        cut.Find("[data-testid=income-actions] button").Click();
        popovers.WaitForElement("[data-testid=deactivate]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { (Salary.Id, false) }, _incomeApi.ActiveChanges);
            Assert.Equal(2, _incomeApi.ListCalls.Count);
        });
    }

    public void Dispose() => _context.Dispose();

    private void UseRole(string role, bool isOwner = true)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
    }
}
