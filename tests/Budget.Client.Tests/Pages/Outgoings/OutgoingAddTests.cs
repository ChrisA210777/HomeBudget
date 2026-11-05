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

public sealed class OutgoingAddTests : IDisposable
{
    private static readonly Guid InsuranceId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid HiddenId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly TestContext _context = new();
    private readonly FakeOutgoingApi _api = new();

    public OutgoingAddTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IOutgoingApi>(_api);
        _context.Services.AddSingleton<ICategoryApi>(new StaticCategoryApi());
        _context.Services.AddSingleton<IPeopleApi>(new StaticPeopleApi());
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    [Fact]
    public async Task Adds_a_bill_for_the_household_from_today_and_goes_home()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();

        cut.Find("input[data-testid=name]").Change("Car insurance");
        await ChooseAsync(cut, Selects<Guid>(cut)[0], InsuranceId);
        await ChooseAsync(cut, Selects<OutgoingPriorityDto?>(cut)[0], OutgoingPriorityDto.Essential);
        cut.Find("input[data-testid=amount]").Change("480");
        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Year);
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Created);
            Assert.Equal(OutgoingKindDto.Bill, request.Kind);
            Assert.Equal("Car insurance", request.Name);
            Assert.Equal(InsuranceId, request.CategoryId);
            Assert.Equal(OutgoingPriorityDto.Essential, request.Priority);
            Assert.Null(request.PersonId);
            Assert.Equal(480m, request.Amount);
            Assert.Equal(1, request.Every);
            Assert.Equal(PaymentUnitDto.Year, request.Unit);
            Assert.Equal(new DateOnly(2026, 11, 5), request.FirstPaymentDate);
            Assert.Null(request.RenewalDate);
            Assert.Equal(AlertPeriodDto.None, request.AlertPeriod);
        });
        Assert.EndsWith("/", _context.Services.GetRequiredService<FakeNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_the_monthly_equivalent_as_you_type()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();

        Assert.Empty(cut.FindAll("[data-testid=monthly-equivalent]"));

        cut.Find("input[data-testid=amount]").Input("480");
        Assert.Equal("£480.00 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim());

        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Year);
        Assert.Equal("£480.00 a year = £40.00 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim());

        cut.Find("input[data-testid=amount]").Input("15");
        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Week);
        Assert.Equal("£15.00 a week = £65.00 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim());
    }

    [Fact]
    public void Missing_details_are_flagged_before_anything_is_sent()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();

        cut.Find("[data-testid=save]").Click();

        Assert.Empty(_api.Created);
        Assert.Contains("Enter a name", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Choose a category.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Choose how important it is.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Enter the amount.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_renewal_is_sent_with_its_reminder()
    {
        var cut = await FilledInAsync();

        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => cut.FindComponents<MudDatePicker>()[1].Instance.DateChanged.InvokeAsync(new DateTime(2027, 3, 14)));
        await ChooseAsync(cut, Selects<AlertPeriodDto>(cut)[0], AlertPeriodDto.OneMonth);
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Created);
            Assert.Equal(new DateOnly(2027, 3, 14), request.RenewalDate);
            Assert.Equal(12, request.RenewalEveryMonths);
            Assert.Equal(AlertPeriodDto.OneMonth, request.AlertPeriod);
        });
    }

    [Fact]
    public async Task Turning_on_renewal_needs_a_date()
    {
        var cut = await FilledInAsync();

        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        cut.Find("[data-testid=save]").Click();

        Assert.Empty(_api.Created);
        Assert.Contains("Choose the next renewal date", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Labels_follow_the_kind_and_allowances_hide_recipient_and_renewal()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();
        cut.Find("[data-testid=toggle-details]").Click();

        Assert.Contains("Paid to", cut.Markup, StringComparison.Ordinal);

        await ChooseAsync(cut, Selects<OutgoingKindDto>(cut)[0], OutgoingKindDto.Savings);
        Assert.Contains("Savings pot or account", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("It renews", cut.Markup, StringComparison.Ordinal);

        await ChooseAsync(cut, Selects<OutgoingKindDto>(cut)[0], OutgoingKindDto.Allowance);
        Assert.DoesNotContain("Savings pot or account", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Paid to", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("It renews", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reference-value", "4111 1111 1111 1111", true)]
    [InlineData("notes", "Card 4111-1111-1111-1111 expires 2028", true)]
    [InlineData("reference-value", "POL-12345678", false)]
    public void Warns_when_something_looks_like_a_card_number(string field, string text, bool warns)
    {
        var cut = _context.RenderComponent<OutgoingAdd>();
        cut.Find("[data-testid=toggle-details]").Click();

        cut.Find($"[data-testid={field}]").Input(text);

        Assert.Equal(warns, cut.FindAll("[data-testid=sensitive-warning]").Count == 1);
    }

    [Fact]
    public async Task Errors_from_the_server_are_shown_against_the_field()
    {
        _api.NextErrors = new Dictionary<string, string[]> { ["CategoryId"] = ["Choose a category."] };
        var cut = await FilledInAsync();

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Choose a category.", cut.Markup, StringComparison.Ordinal));
        Assert.EndsWith("/outgoings/new", _context.Services.GetRequiredService<FakeNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewers_cannot_add()
    {
        UseRole("Viewer");

        var cut = _context.RenderComponent<OutgoingAdd>();

        Assert.NotNull(cut.Find("[data-testid=read-only]"));
        Assert.Empty(cut.FindAll("[data-testid=save]"));
    }

    public void Dispose() => _context.Dispose();

    private async Task<IRenderedComponent<OutgoingAdd>> FilledInAsync()
    {
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo("/outgoings/new");
        var cut = _context.RenderComponent<OutgoingAdd>();
        cut.Find("input[data-testid=name]").Change("Car insurance");
        await ChooseAsync(cut, Selects<Guid>(cut)[0], InsuranceId);
        await ChooseAsync(cut, Selects<OutgoingPriorityDto?>(cut)[0], OutgoingPriorityDto.Preferred);
        cut.Find("input[data-testid=amount]").Change("40");
        return cut;
    }

    private static IReadOnlyList<IRenderedComponent<MudSelect<T>>> Selects<T>(IRenderedComponent<OutgoingAdd> cut) =>
        cut.FindComponents<MudSelect<T>>();

    private static Task ChooseAsync<T>(IRenderedComponent<OutgoingAdd> cut, IRenderedComponent<MudSelect<T>> select, T value) =>
        cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(value));

    private void UseRole(string role) => _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, role == "Admin"));

    private sealed class StaticCategoryApi : ICategoryApi
    {
        public Task<IReadOnlyList<OutgoingCategorySummary>> ListAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<OutgoingCategorySummary>>(
            [
                new(InsuranceId, "Insurance", IsHidden: false, OutgoingCount: 0),
                new(HiddenId, "Old stuff", IsHidden: true, OutgoingCount: 0),
            ]);

        public Task<CategoryResult> AddAsync(Guid householdId, AddCategoryRequest request) => throw new NotSupportedException();

        public Task<CategoryResult> UpdateAsync(Guid householdId, Guid categoryId, UpdateCategoryRequest request) => throw new NotSupportedException();

        public Task<CategoryResult> DeleteAsync(Guid householdId, Guid categoryId) => throw new NotSupportedException();
    }

    private sealed class FakeOutgoingApi : IOutgoingApi
    {
        public List<CreateOutgoingRequest> Created { get; } = [];

        public IReadOnlyDictionary<string, string[]>? NextErrors { get; set; }

        public Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request)
        {
            if (NextErrors is { } errors)
            {
                return Task.FromResult(ApiResult<OutgoingCreatedResponse>.Invalid(errors));
            }

            Created.Add(request);
            return Task.FromResult(ApiResult<OutgoingCreatedResponse>.Success(new OutgoingCreatedResponse(Guid.NewGuid(), request.Name, 40m)));
        }

        public Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<string>>(["Joint account"]);
    }
}
