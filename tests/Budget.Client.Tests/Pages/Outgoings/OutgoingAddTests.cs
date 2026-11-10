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
    public async Task Adds_a_bill_for_the_household_from_today_and_goes_to_the_list()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();
        Assert.Empty(cut.FindAll("[data-testid=comment]")); // The first amount is recorded as "Starting amount" (BUD-78).

        await SetNameAsync(cut, "Insurance for the car");
        await ChooseAsync(cut, Selects<Guid>(cut)[0], InsuranceId);
        await ChooseAsync(cut, Selects<OutgoingPriorityDto?>(cut)[0], OutgoingPriorityDto.Essential);
        cut.Find("input[data-testid=amount]").Input("480");
        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Year);
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Created);
            Assert.Equal(OutgoingKindDto.Bill, request.Kind);
            Assert.Equal("Insurance for the car", request.Name);
            Assert.Equal(InsuranceId, request.CategoryId);
            Assert.Equal(OutgoingPriorityDto.Essential, request.Priority);
            Assert.Null(request.PersonId);
            Assert.Equal(480m, request.Amount);
            Assert.Equal(1, request.Every);
            Assert.Equal(PaymentUnitDto.Year, request.Unit);
            Assert.Equal(new DateOnly(2026, 11, 5), request.FirstPaymentDate);
            Assert.Null(request.RenewalDate);
            Assert.Equal(AlertPeriodDto.None, request.AlertPeriod);
            Assert.Null(request.Comment);
        });
        Assert.EndsWith("/outgoings", _context.Services.GetRequiredService<FakeNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_the_monthly_equivalent_as_you_type()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();

        Assert.Empty(cut.FindAll("[data-testid=monthly-equivalent]"));

        cut.Find("input[data-testid=amount]").Input("480");
        cut.WaitForAssertion(() => Assert.Equal("£480.00 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim()));

        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Year);
        cut.WaitForAssertion(() => Assert.Equal("£480.00 a year = £40.00 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim()));

        cut.Find("input[data-testid=amount]").Input("15");
        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Week);
        cut.WaitForAssertion(() => Assert.Equal("£15.00 a week = £65.00 a month", cut.Find("[data-testid=monthly-equivalent]").TextContent.Trim()));
    }

    [Fact]
    public async Task Picking_a_common_outgoing_fills_in_its_usual_settings()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();

        await SetNameAsync(cut, "car INSURANCE");
        cut.Find("input[data-testid=amount]").Input("480");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Created);
            Assert.Equal("Car insurance", request.Name);
            Assert.Equal((OutgoingKindDto.Bill, InsuranceId, OutgoingPriorityDto.Essential), (request.Kind, request.CategoryId, request.Priority));
            Assert.Equal((1, PaymentUnitDto.Year), (request.Every, request.Unit));
            Assert.Equal((PaymentMethodDto.DirectDebit, "Policy number"), (request.PaymentMethod, request.ReferenceLabel));
            Assert.Equal(new DateOnly(2027, 11, 5), request.RenewalDate);
            Assert.Equal((12, true, AlertPeriodDto.OneMonth), (request.RenewalEveryMonths, request.AutoRenews, request.AlertPeriod));
        });
    }

    [Fact]
    public async Task A_suggestion_doesnt_overwrite_what_youve_already_chosen()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();
        await ChooseAsync(cut, Selects<OutgoingPriorityDto?>(cut)[0], OutgoingPriorityDto.Optional);
        await ChooseAsync(cut, Selects<PaymentUnitDto>(cut)[0], PaymentUnitDto.Month);
        cut.Find("[data-testid=toggle-details]").Click();
        cut.Find("input[data-testid=website]").Change("my-insurer.co.uk");

        await SetNameAsync(cut, "Car insurance");
        cut.Find("input[data-testid=amount]").Input("40");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Created);
            Assert.Equal((OutgoingPriorityDto.Optional, PaymentUnitDto.Month), (request.Priority!.Value, request.Unit));
            Assert.Equal("my-insurer.co.uk", request.Website);
            Assert.Equal(InsuranceId, request.CategoryId);
        });
    }

    [Fact]
    public async Task Typing_your_own_name_fills_in_nothing()
    {
        var cut = _context.RenderComponent<OutgoingAdd>();

        await SetNameAsync(cut, "Insurance for the car");

        // A suggestion would have opened the details with its reference name filled in.
        Assert.Empty(cut.FindAll("[data-testid=reference-label]"));
        Assert.Empty(cut.FindAll("[data-testid=renewal-date]"));
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

    [Fact]
    public void Editing_fills_in_the_details_and_hides_the_amount()
    {
        _api.Existing = OutgoingDetailTests.Details(hiddenCategory: HiddenId);
        var id = _api.Existing.Id;

        var cut = _context.RenderComponent<OutgoingAdd>(p => p.Add(c => c.OutgoingId, id));

        Assert.Equal("Car insurance", cut.Find("input[data-testid=name]").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid=amount]"));
        Assert.Empty(cut.FindAll("[data-testid=first-payment]"));
        Assert.Contains("Save changes", cut.Find("[data-testid=save]").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid=website]"));
    }

    [Fact]
    public async Task Saving_an_edit_sends_the_version_and_returns_to_the_outgoing()
    {
        _api.Existing = OutgoingDetailTests.Details();
        var id = _api.Existing.Id;
        var cut = _context.RenderComponent<OutgoingAdd>(p => p.Add(c => c.OutgoingId, id));

        cut.Find("input[data-testid=name]").Change("Car insurance (Ford)");
        await ChooseAsync(cut, Selects<OutgoingPriorityDto?>(cut)[0], OutgoingPriorityDto.Preferred);
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var update = Assert.Single(_api.Updates);
            Assert.Equal(("Car insurance (Ford)", OutgoingPriorityDto.Preferred, "v1"), (update.Name, update.Priority, update.Version));
            Assert.Equal("https://example.com/", update.Website);
        });
        Assert.EndsWith($"/outgoings/{id}", _context.Services.GetRequiredService<FakeNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conflict_while_editing_reloads_and_says_so()
    {
        _api.Existing = OutgoingDetailTests.Details();
        _api.ConflictNext = true;
        var cut = _context.RenderComponent<OutgoingAdd>(p => p.Add(c => c.OutgoingId, _api.Existing.Id));

        cut.Find("input[data-testid=name]").Change("Mine");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=conflict]")));
        Assert.Equal("Car insurance", cut.Find("input[data-testid=name]").GetAttribute("value"));
        Assert.Empty(_api.Updates);
    }

    public void Dispose() => _context.Dispose();

    private async Task<IRenderedComponent<OutgoingAdd>> FilledInAsync()
    {
        _context.Services.GetRequiredService<FakeNavigationManager>().NavigateTo("/outgoings/new");
        var cut = _context.RenderComponent<OutgoingAdd>();
        await SetNameAsync(cut, "Insurance for the car");
        await ChooseAsync(cut, Selects<Guid>(cut)[0], InsuranceId);
        await ChooseAsync(cut, Selects<OutgoingPriorityDto?>(cut)[0], OutgoingPriorityDto.Preferred);
        cut.Find("input[data-testid=amount]").Input("40");
        return cut;
    }

    private static Task SetNameAsync(IRenderedComponent<OutgoingAdd> cut, string name) =>
        cut.InvokeAsync(() => cut.FindComponent<MudAutocomplete<string>>().Instance.ValueChanged.InvokeAsync(name));

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

        public Task<IReadOnlyList<string>> ReferenceLabelsAsync(Guid householdId) => Task.FromResult<IReadOnlyList<string>>(["Policy number"]);

        public Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<string>>(["Joint account"]);

        public Task<OutgoingMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1) =>
            Task.FromResult(new OutgoingMonthResponse(year, month, [], "GBP"));

        public OutgoingDetailsResponse? Existing { get; set; }

        public List<UpdateOutgoingRequest> Updates { get; } = [];

        public bool ConflictNext { get; set; }

        public Task<OutgoingDetailsResponse?> GetAsync(Guid householdId, Guid outgoingId) => Task.FromResult(Existing);

        public Task<ApiResult<OutgoingDetailsResponse>> UpdateAsync(Guid householdId, Guid outgoingId, UpdateOutgoingRequest request)
        {
            if (ConflictNext)
            {
                ConflictNext = false;
                return Task.FromResult(new ApiResult<OutgoingDetailsResponse>(null, new Dictionary<string, string[]>(), Conflict: true));
            }

            Updates.Add(request);
            return Task.FromResult(ApiResult<OutgoingDetailsResponse>.Success(Existing!));
        }

        public Task DeleteAsync(Guid householdId, Guid outgoingId) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> AddAmountChangeAsync(Guid householdId, Guid outgoingId, SaveOutgoingAmountRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> CorrectAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingAmountRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> DeleteAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> AddStatusPeriodAsync(Guid householdId, Guid outgoingId, SaveOutgoingStatusRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> CorrectStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingStatusRequest request) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingDetailsResponse>> DeleteStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) => throw new NotSupportedException();
    }
}
