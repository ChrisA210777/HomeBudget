using Budget.Client.Account;
using Budget.Client.Accounts;
using Budget.Client.Income;
using Budget.Client.Pages.Transfers;
using Budget.Client.Tests.TestDoubles;
using Budget.Client.Transfers;
using Budget.Shared.Outgoings;
using Budget.Shared.Transfers;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Transfers;

/// <summary>The Transfers list, form and page (BUD-91).</summary>
public sealed class TransferPagesTests : IDisposable
{
    private static readonly Guid CurrentId = Guid.NewGuid();
    private static readonly Guid JointId = Guid.NewGuid();
    private static readonly Guid HolidayId = Guid.NewGuid();
    private static readonly Guid TransferId = Guid.NewGuid();

    private readonly TestContext _context = new();
    private readonly FakeTransferApi _api = new();

    public TransferPagesTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<ITransferApi>(_api);
        _context.Services.AddSingleton<IAccountsApi>(new Accounts());
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Lists_transfers_by_the_account_they_go_into()
    {
        UseRole("Admin");

        var cut = _context.RenderComponent<TransferList>();

        Assert.Equal(new[] { "Into Joint" }, Texts(cut, "transfer-group-name"));
        Assert.Equal(new[] { "£450.00" }, Texts(cut, "transfer-group-total"));
        Assert.Equal(new[] { "£400.00", "£50.00" }, Texts(cut, "transfer-amount"));
        Assert.Equal(new[] { "a month", "a month" }, Texts(cut, "transfer-period"));
        Assert.Equal(new[] { "Bills top-up", "Chris current → Joint › Holiday" }, Texts(cut, "transfer-name"));
        Assert.Equal(new[] { "Chris current → Joint · next 25 Nov 2026", "next 1 Dec 2026" }, Texts(cut, "transfer-details"));
        Assert.Equal("/transfers/new", cut.Find("[data-testid=add-transfer]").GetAttribute("href"));
    }

    [Fact]
    public void With_transfers_off_they_can_be_seen_but_not_added()
    {
        UseRole("Admin", transfersEnabled: false);

        var cut = _context.RenderComponent<TransferList>();

        Assert.NotNull(cut.Find("[data-testid=transfers-off]"));
        Assert.Empty(cut.FindAll("[data-testid=add-transfer]"));
        Assert.Equal(2, cut.FindAll("[data-testid=transfer]").Count);
    }

    [Fact]
    public async Task Adding_a_transfer_sends_the_accounts_amount_and_schedule()
    {
        UseRole("User");
        var cut = _context.RenderComponent<TransferEdit>();

        cut.Find("[data-testid=save]").Click();
        Assert.Contains("Choose the account it comes from.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(_api.Created);

        var selects = cut.FindComponents<MudSelect<Guid?>>();
        await cut.InvokeAsync(() => selects[0].Instance.ValueChanged.InvokeAsync(CurrentId));
        await cut.InvokeAsync(() => cut.FindComponents<MudSelect<Guid?>>()[1].Instance.ValueChanged.InvokeAsync(JointId));
        cut.Find("input[data-testid=amount]").Input("400");
        cut.Find("input[data-testid=name]").Input("Bills top-up");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(_api.Created);
            Assert.Equal((CurrentId, JointId, 400m, 1, PaymentUnitDto.Month, new DateOnly(2026, 11, 5), "Bills top-up"),
                (request.FromAccountId!.Value, request.ToAccountId!.Value, request.Amount!.Value, request.Every, request.Unit, request.FirstPaymentDate!.Value, request.Name));
        });
        Assert.EndsWith("/transfers/" + TransferId, _context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transfer_shows_where_it_goes_and_its_history_and_can_change_its_amount()
    {
        UseRole("User");

        var cut = _context.RenderComponent<TransferDetail>(p => p.Add(c => c.TransferId, TransferId));

        Assert.Equal("Bills top-up", Text(cut, "transfer-name"));
        Assert.Equal("£400.00 a month", Text(cut, "transfer-paid"));
        Assert.Equal(("Chris current", "Joint"), (Text(cut, "from-account"), Text(cut, "to-account")));
        Assert.Single(cut.FindAll("[data-testid=amount-change]"));
        Assert.NotNull(cut.Find("[data-testid=end-transfer]"));

        cut.Find("[data-testid=change-amount]").Click();
        cut.Find("input[data-testid=amount]").Input("450");
        cut.Find("input[data-testid=comment]").Change("Bills went up");
        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => Assert.Equal((HttpMethod.Post, $"/api/households/{FakeAccountApi.HouseholdId}/transfers/{TransferId}/amount-changes"), Assert.Single(_api.Sent)));
    }

    [Fact]
    public void Viewers_cant_change_a_transfer()
    {
        UseRole("Viewer");

        var cut = _context.RenderComponent<TransferDetail>(p => p.Add(c => c.TransferId, TransferId));

        Assert.Empty(cut.FindAll("[data-testid=change-amount]"));
        Assert.Empty(cut.FindAll("[data-testid=delete-transfer]"));
    }

    public void Dispose() => _context.Dispose();

    private void UseRole(string role, bool transfersEnabled = true) =>
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, role == "Admin", transfersEnabled));

    private static string Text<T>(IRenderedComponent<T> cut, string testId)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private static string[] Texts<T>(IRenderedComponent<T> cut, string testId)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll($"[data-testid={testId}]").Select(e => string.Join(' ', e.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray();

    private static TransferDetailsResponse Details() => new(
        TransferId, "Bills top-up", CurrentId, "Chris current", JointId, "Joint", 400m, 1, PaymentUnitDto.Month, 400m,
        new DateOnly(2026, 1, 25), null, new DateOnly(2026, 11, 25), null, null,
        [new DateOnly(2026, 11, 25), new DateOnly(2026, 12, 25)],
        [new(Guid.NewGuid(), new DateOnly(2026, 1, 25), 400m, 1, PaymentUnitDto.Month, 400m, "Starting amount", "Chris Austin", DateTimeOffset.UtcNow, false, true)],
        [],
        [],
        "GBP",
        "v1");

    private sealed class FakeTransferApi : ITransferApi
    {
        public List<CreateTransferRequest> Created { get; } = [];

        public List<(HttpMethod Method, string Url)> Sent { get; } = [];

        public Task<TransfersResponse> ListAsync(Guid householdId) => Task.FromResult(new TransfersResponse(
        [
            new(TransferId, "Bills top-up", CurrentId, "Chris current", JointId, "Joint", JointId, "Joint", 400m, 1, PaymentUnitDto.Month, 400m, new DateOnly(2026, 11, 25), null, false),
            new(Guid.NewGuid(), null, CurrentId, "Chris current", HolidayId, "Joint › Holiday", JointId, "Joint", 50m, 1, PaymentUnitDto.Month, 50m, new DateOnly(2026, 12, 1), null, false),
        ], "GBP"));

        public Task<TransferDetailsResponse?> GetAsync(Guid householdId, Guid transferId) => Task.FromResult<TransferDetailsResponse?>(Details());

        public Task<ApiResult<TransferDetailsResponse>> CreateAsync(Guid householdId, CreateTransferRequest request)
        {
            Created.Add(request);
            return Task.FromResult(ApiResult<TransferDetailsResponse>.Success(Details()));
        }

        public Task<ApiResult<TransferDetailsResponse>> UpdateAsync(Guid householdId, Guid transferId, UpdateTransferRequest request) =>
            Task.FromResult(ApiResult<TransferDetailsResponse>.Success(Details()));

        public Task DeleteAsync(Guid householdId, Guid transferId) => Task.CompletedTask;

        public Task<ApiResult<TransferDetailsResponse>> SendAsync(HttpMethod method, string url, object body)
        {
            Sent.Add((method, url));
            return Task.FromResult(ApiResult<TransferDetailsResponse>.Success(Details()));
        }
    }

    private sealed class Accounts : IAccountsApi
    {
        public Task<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>> ListAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>>(
            [
                new(CurrentId, "Chris current", null, null, null, null, false, "Chris current", 0),
                new(JointId, "Joint", null, null, null, null, false, "Joint", 0),
                new(HolidayId, "Holiday", null, null, null, JointId, false, "Joint › Holiday", 0),
            ]);

        public Task<AccountsResult> AddAsync(Guid householdId, Budget.Shared.Accounts.SaveAccountRequest request) => throw new NotSupportedException();

        public Task<AccountsResult> UpdateAsync(Guid householdId, Guid accountId, Budget.Shared.Accounts.SaveAccountRequest request) => throw new NotSupportedException();

        public Task<AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived) => throw new NotSupportedException();

        public Task<AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId) => throw new NotSupportedException();
    }
}
