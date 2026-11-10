using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Client.Outgoings;
using Budget.Client.Pages.Outgoings;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Outgoings;

public sealed class OutgoingDetailTests : IDisposable
{
    private static readonly Guid OutgoingId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid FirstAmountId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid RiseId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid PauseId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private readonly TestContext _context = new();
    private readonly FakeOutgoingApi _api = new();

    public OutgoingDetailTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IOutgoingApi>(_api);
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    /// <summary>Car insurance: £480 a year from 14 Mar 2026, rising to £540 from 14 Mar 2027, with a past pause.</summary>
    internal static OutgoingDetailsResponse Details(Guid? hiddenCategory = null, string? status = null) => new(
        OutgoingId,
        "Car insurance",
        OutgoingKindDto.Bill,
        OutgoingPriorityDto.Essential,
        hiddenCategory ?? Guid.Parse("dddddddd-0000-0000-0000-000000000010"),
        "Insurance",
        null,
        null,
        480m,
        1,
        PaymentUnitDto.Year,
        40m,
        new DateOnly(2026, 3, 14),
        null,
        new DateOnly(2027, 3, 14),
        new DateOnly(2026, 3, 14),
        12,
        true,
        AlertPeriodDto.OneMonth,
        new DateOnly(2027, 3, 14),
        false,
        status,
        "Example Insurance",
        PaymentMethodDto.DirectDebit,
        "Joint account",
        "Policy number",
        "POL-123",
        "https://example.com/",
        "0800 000 000",
        "No claims since 2020",
        [new DateOnly(2027, 3, 14), new DateOnly(2028, 3, 14), new DateOnly(2029, 3, 14)],
        [
            new(RiseId, new DateOnly(2027, 3, 14), 540m, 1, PaymentUnitDto.Year, 45m, "Renewal quote", "Chris Austin", DateTimeOffset.UtcNow, true, false),
            new(FirstAmountId, new DateOnly(2026, 3, 14), 480m, 1, PaymentUnitDto.Year, 40m, "Starting amount", "Chris Austin", DateTimeOffset.UtcNow, false, true),
        ],
        [new(PauseId, OutgoingStatusReasonDto.Paused, new DateOnly(2026, 3, 14), new DateOnly(2027, 3, 14), 1, "Paid by work", "Chris Austin", DateTimeOffset.UtcNow, false)],
        [],
        "GBP",
        "v1");

    [Fact]
    public void Shows_the_details_with_links_dates_and_history()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        Assert.Equal("Car insurance", Text(cut, "outgoing-name"));
        Assert.Equal("£480.00 a year", Text(cut, "outgoing-paid"));
        Assert.Equal("£40.00 a month", Text(cut, "outgoing-monthly"));
        Assert.Contains("Bill · Insurance · The whole household", Text(cut, "outgoing-summary"), StringComparison.Ordinal);
        Assert.Equal("14 Mar 2027", Text(cut, "next-payment"));
        Assert.Contains("renews automatically", Text(cut, "next-renewal"), StringComparison.Ordinal);
        Assert.Contains("1 month before", Text(cut, "next-renewal"), StringComparison.Ordinal);
        Assert.Equal("Direct Debit", Text(cut, "payment-method"));
        Assert.Equal("https://example.com/", cut.Find("[data-testid=website]").GetAttribute("href"));
        Assert.Equal("tel:0800000000", cut.Find("[data-testid=phone]").GetAttribute("href"));
        Assert.Equal("POL-123", Text(cut, "reference"));
        Assert.Equal(2, cut.FindAll("[data-testid=amount-change]").Count);
        Assert.Single(cut.FindAll("[data-testid=upcoming]"));
        Assert.Equal("Paused from 14 Mar 2026: skips 1 payment, resumes 14 Mar 2027", Text(cut, "period-description"));
        Assert.Equal("/outgoings/" + OutgoingId + "/edit", cut.Find("[data-testid=edit-details]").GetAttribute("href"));
    }

    [Fact]
    public void A_current_pause_is_shown_as_the_status()
    {
        _api.Current = Details(status: "Paused until 14 Mar 2027");

        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        Assert.Equal("Paused until 14 Mar 2027", Text(cut, "outgoing-status"));
    }

    [Fact]
    public async Task Changing_the_amount_defaults_to_the_next_payment_and_sends_the_schedule_and_version()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=change-amount]").Click();
        cut.Find("input[data-testid=amount]").Input("45");
        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<PaymentUnitDto>>().Instance.ValueChanged.InvokeAsync(PaymentUnitDto.Month));
        cut.WaitForAssertion(() => Assert.Equal("£45.00 a month", Text(cut, "monthly-equivalent")));
        cut.Find("[data-testid=save]").Click();

        Assert.Contains("Say why it changed", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(_api.Amounts);

        cut.Find("input[data-testid=comment]").Change("Paying monthly now");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Amounts);
            Assert.Equal((45m, 1, PaymentUnitDto.Month, new DateOnly(2027, 3, 14), "v1"), (request.Amount, request.Every, request.Unit, request.EffectiveFrom!.Value, request.Version));
        });
        Assert.Empty(cut.FindAll("[data-testid=editor]"));
    }

    [Fact]
    public void Pausing_skips_payments_from_a_chosen_payment_and_previews_when_they_resume()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=pause]").Click();
        cut.Find("input[data-testid=skip-payments]").Input("2");
        cut.WaitForAssertion(() => Assert.Equal("Payments resume on 14 Mar 2029.", Text(cut, "resumes-preview")));
        cut.Find("input[data-testid=comment]").Change("Not driving");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Statuses);
            Assert.Equal((OutgoingStatusReasonDto.Paused, new DateOnly(2027, 3, 14), 2), (request.Reason, request.StartDate!.Value, request.SkipPayments!.Value));
        });
    }

    [Fact]
    public async Task A_pause_until_further_notice_sends_no_count()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=pause]").Click();
        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        cut.Find("input[data-testid=comment]").Change("Suspended");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Null(Assert.Single(_api.Statuses).SkipPayments));
    }

    [Fact]
    public void Correcting_a_pause_starts_from_it_and_needs_a_reason()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=correct-period]").Click();
        Assert.Equal("Paid by work", cut.Find("input[data-testid=comment]").GetAttribute("value"));
        cut.Find("[data-testid=save]").Click();
        Assert.Contains("Say why you're correcting it.", cut.Markup, StringComparison.Ordinal);

        cut.Find("input[data-testid=correction-reason]").Change("Wrong year");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var (entryId, request) = Assert.Single(_api.StatusCorrections);
            Assert.Equal((PauseId, new DateOnly(2026, 3, 14), 1, "Wrong year"), (entryId, request.StartDate!.Value, request.SkipPayments!.Value, request.CorrectionReason));
        });
    }

    [Fact]
    public void Ending_records_an_ending_from_today()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=end-outgoing]").Click();
        cut.Find("input[data-testid=comment]").Change("Sold the car");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Statuses);
            Assert.Equal((OutgoingStatusReasonDto.Ended, new DateOnly(2026, 11, 5)), (request.Reason, request.StartDate!.Value));
            Assert.Null(request.SkipPayments);
        });
    }

    [Fact]
    public void The_first_amount_can_be_corrected_but_not_deleted()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        var cards = cut.FindAll("[data-testid=amount-change]").ToList();
        Assert.NotNull(cards[0].QuerySelector("[data-testid=delete-entry]"));
        Assert.Null(cards[1].QuerySelector("[data-testid=delete-entry]"));
        Assert.NotNull(cards[1].QuerySelector("[data-testid=correct-entry]"));
    }

    [Fact]
    public void A_conflict_reloads_and_says_so()
    {
        _api.ConflictNext = true;
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=end-outgoing]").Click();
        cut.Find("input[data-testid=comment]").Change("Sold the car");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=conflict]")));
        Assert.Equal(2, _api.Loads);
    }

    [Fact]
    public void Server_rule_errors_are_shown_against_the_field()
    {
        _api.NextErrors = new Dictionary<string, string[]> { ["StartDate"] = ["A pause has to start on a payment date."] };
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=pause]").Click();
        cut.Find("input[data-testid=comment]").Change("Holiday");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Contains("A pause has to start on a payment date.", cut.Markup, StringComparison.Ordinal));
        Assert.NotNull(cut.Find("[data-testid=editor]"));
    }

    [Fact]
    public void Deleting_the_outgoing_goes_back_to_the_list()
    {
        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        cut.Find("[data-testid=delete-outgoing]").Click();
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.True(_api.Deleted));
        Assert.EndsWith("/outgoings", _context.Services.GetRequiredService<FakeNavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewers_see_everything_but_cannot_change_it()
    {
        UseRole("Viewer");

        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        Assert.Equal("Car insurance", Text(cut, "outgoing-name"));
        Assert.Empty(cut.FindAll("[data-testid=change-amount]"));
        Assert.Empty(cut.FindAll("[data-testid=edit-details]"));
        Assert.Empty(cut.FindAll("[data-testid=correct-entry]"));
        Assert.Empty(cut.FindAll("[data-testid=correct-period]"));
    }

    [Fact]
    public void A_missing_outgoing_says_so()
    {
        _api.Current = null;

        var cut = _context.RenderComponent<OutgoingDetail>(p => p.Add(c => c.OutgoingId, OutgoingId));

        Assert.NotNull(cut.Find("[data-testid=not-found]"));
    }

    public void Dispose() => _context.Dispose();

    private static string Text(IRenderedComponent<OutgoingDetail> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private void UseRole(string role) => _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, role == "Admin"));

    private sealed class FakeOutgoingApi : IOutgoingApi
    {
        public OutgoingDetailsResponse? Current { get; set; } = Details();

        public int Loads { get; private set; }

        public bool ConflictNext { get; set; }

        public IReadOnlyDictionary<string, string[]>? NextErrors { get; set; }

        public bool Deleted { get; private set; }

        public List<SaveOutgoingAmountRequest> Amounts { get; } = [];

        public List<SaveOutgoingStatusRequest> Statuses { get; } = [];

        public List<(Guid EntryId, SaveOutgoingStatusRequest Request)> StatusCorrections { get; } = [];

        public Task<OutgoingDetailsResponse?> GetAsync(Guid householdId, Guid outgoingId)
        {
            Loads++;
            return Task.FromResult(Current);
        }

        public Task<ApiResult<OutgoingDetailsResponse>> AddAmountChangeAsync(Guid householdId, Guid outgoingId, SaveOutgoingAmountRequest request) =>
            Respond(() => Amounts.Add(request));

        public Task<ApiResult<OutgoingDetailsResponse>> CorrectAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingAmountRequest request) =>
            Respond(() => Amounts.Add(request));

        public Task<ApiResult<OutgoingDetailsResponse>> DeleteAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) =>
            Respond(() => { });

        public Task<ApiResult<OutgoingDetailsResponse>> AddStatusPeriodAsync(Guid householdId, Guid outgoingId, SaveOutgoingStatusRequest request) =>
            Respond(() => Statuses.Add(request));

        public Task<ApiResult<OutgoingDetailsResponse>> CorrectStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingStatusRequest request) =>
            Respond(() => StatusCorrections.Add((entryId, request)));

        public Task<ApiResult<OutgoingDetailsResponse>> DeleteStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) =>
            Respond(() => { });

        public Task DeleteAsync(Guid householdId, Guid outgoingId)
        {
            Deleted = true;
            return Task.CompletedTask;
        }

        public Task<ApiResult<OutgoingDetailsResponse>> UpdateAsync(Guid householdId, Guid outgoingId, UpdateOutgoingRequest request) => throw new NotSupportedException();

        public Task<OutgoingMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1) => throw new NotSupportedException();

        public Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ReferenceLabelsAsync(Guid householdId) => Task.FromResult<IReadOnlyList<string>>(["Policy number"]);

        public Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId) => throw new NotSupportedException();

        private Task<ApiResult<OutgoingDetailsResponse>> Respond(Action record)
        {
            if (ConflictNext)
            {
                ConflictNext = false;
                return Task.FromResult(new ApiResult<OutgoingDetailsResponse>(null, new Dictionary<string, string[]>(), Conflict: true));
            }

            if (NextErrors is { } errors)
            {
                return Task.FromResult(ApiResult<OutgoingDetailsResponse>.Invalid(errors));
            }

            record();
            return Task.FromResult(ApiResult<OutgoingDetailsResponse>.Success(Current!));
        }
    }
}
