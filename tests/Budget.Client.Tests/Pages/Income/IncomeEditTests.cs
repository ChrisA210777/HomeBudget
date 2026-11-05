using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.Pages.Income;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Income;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Income;

public sealed class IncomeEditTests : IDisposable
{
    private static readonly Guid StreamId = Guid.NewGuid();

    private static readonly EmploymentIncomeDetails FixedSalary = new(
        StreamId, "Sam – Acme Ltd", "Acme Ltd", FakeAccountApi.UserId, PayFrequencyDto.FourWeekly, IncomeKindDto.Fixed,
        30000m, AmountBasisDto.Annual, null, null, null, IsActive: true, Version: "AAAAAAAAB9E=");

    private static readonly EmploymentIncomeDetails VariableOvertime = new(
        StreamId, "Overtime", null, null, PayFrequencyDto.Weekly, IncomeKindDto.Variable,
        null, null, 300m, 450m, 700m, IsActive: true, Version: "AAAAAAAAB9E=");

    private readonly TestContext _context = new();
    private readonly FakeIncomeApi _incomeApi = new();

    public IncomeEditTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IIncomeApi>(_incomeApi);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    private FakeNavigationManager Navigation => _context.Services.GetRequiredService<FakeNavigationManager>();

    [Fact]
    public void Editing_shows_values_exactly_as_entered_with_a_preview()
    {
        UseRole("User");
        _incomeApi.Details = FixedSalary;

        var cut = RenderEdit();

        Assert.Equal("Sam – Acme Ltd", cut.Find("input[data-testid=name]").GetAttribute("value"));
        Assert.Equal(
            "£2,307.69 per 4-weekly pay, £30,000.00 a year before tax.",
            cut.Find("[data-testid=preview]").TextContent.Trim());
    }

    [Fact]
    public void Variable_pay_fields_name_the_pay_period_and_preview_the_yearly_range()
    {
        UseRole("User");
        _incomeApi.Details = VariableOvertime;

        var cut = RenderEdit();

        Assert.Contains("Lowest gross per weekly pay", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Average gross per weekly pay", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Highest gross per weekly pay", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(
            "About £23,400.00 a year before tax (£15,600.00 – £36,400.00).",
            cut.Find("[data-testid=preview]").TextContent.Trim());
    }

    [Fact]
    public void Saving_sends_the_loaded_version_and_returns_to_the_list()
    {
        UseRole("User");
        _incomeApi.Details = FixedSalary;
        var cut = RenderEdit();

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (id, request) = Assert.Single(_incomeApi.Saves);
            Assert.Equal(StreamId, id);
            Assert.Equal("Sam – Acme Ltd", request.Name);
            Assert.Equal(30000m, request.GrossAmount);
            Assert.Equal(AmountBasisDto.Annual, request.AmountBasis);
            Assert.Null(request.AverageGross);
            Assert.Equal(FixedSalary.Version, request.Version);
            Assert.Equal("http://localhost/income", Navigation.Uri);
        });
    }

    [Fact]
    public void A_new_form_is_checked_before_anything_is_sent()
    {
        UseRole("User");
        var cut = _context.RenderComponent<IncomeEdit>();

        cut.Find("[data-testid=save]").Click();

        Assert.Empty(_incomeApi.Saves);
        Assert.Contains("Enter a name for this income", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Enter the gross pay.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_from_the_server_are_shown_against_the_field()
    {
        UseRole("User");
        _incomeApi.Details = FixedSalary;
        _incomeApi.NextSaveResult = new SaveResult(
            null, new Dictionary<string, string[]> { ["MemberUserId"] = ["Choose someone who is a member of this household."] }, Conflict: false);
        var cut = RenderEdit();

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Choose someone who is a member of this household.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Someone_elses_change_is_explained()
    {
        UseRole("User");
        _incomeApi.Details = FixedSalary;
        _incomeApi.NextSaveResult = new SaveResult(null, new Dictionary<string, string[]>(), Conflict: true);
        var cut = RenderEdit();

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Someone else changed this income", cut.Find("[data-testid=conflict]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Viewers_see_that_they_cannot_change_income()
    {
        UseRole("Viewer", isOwner: false);
        _incomeApi.Details = FixedSalary;

        var cut = RenderEdit();

        Assert.NotNull(cut.Find("[data-testid=read-only]"));
        Assert.Empty(cut.FindAll("[data-testid=save]"));
    }

    [Fact]
    public void Deleted_income_says_so()
    {
        UseRole("User");
        _incomeApi.Details = null;

        var cut = RenderEdit();

        Assert.NotNull(cut.Find("[data-testid=not-found]"));
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<IncomeEdit> RenderEdit() =>
        _context.RenderComponent<IncomeEdit>(p => p.Add(c => c.IncomeStreamId, StreamId));

    private void UseRole(string role, bool isOwner = true)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
    }
}
