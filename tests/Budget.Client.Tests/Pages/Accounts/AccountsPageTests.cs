using Budget.Client.Account;
using Budget.Client.Accounts;
using Budget.Client.Pages.Accounts;
using Budget.Client.People;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Accounts;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Accounts;

/// <summary>The Accounts page (BUD-89).</summary>
public sealed class AccountsPageTests : IDisposable
{
    private static readonly Guid MonzoId = Guid.NewGuid();
    private static readonly Guid ChrisCurrentId = Guid.NewGuid();

    private readonly TestContext _context = new();
    private readonly FakeAccountsApi _api = new();
    private readonly SettingsHouseholdApi _households = new();

    public AccountsPageTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountsApi>(_api);
        _context.Services.AddSingleton<IPeopleApi>(new StaticPeopleApi());
        _context.Services.AddSingleton<Budget.Client.Households.IHouseholdApi>(_households);
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Lists_accounts_with_their_pots_owner_and_how_many_outgoings_use_them()
    {
        var cut = Render("Viewer");

        Assert.Equal(new[] { "Chris current", "Joint Monzo", "Holiday" }, Texts(cut, "account-name"));
        // An account's count includes its pots, and goes to its outgoings; pots aren't linked (BUD-95).
        Assert.Equal(new[] { "Chris Austin · 1 outgoing", "Joint · Monzo · 5 outgoings", "2 outgoings" }, Texts(cut, "account-details"));
        Assert.Equal(new[] { "/outgoings?account=" + ChrisCurrentId, $"/outgoings?account={MonzoId}" },
            cut.FindAll("[data-testid=account-outgoings]").Select(e => e.GetAttribute("href")));
        Assert.Single(cut.FindAll("[data-testid=pot]"));
        // Viewers can't change anything, and archived accounts are hidden until asked for.
        Assert.Empty(cut.FindAll("[data-testid=add-account-form]"));
        Assert.Empty(cut.FindAll("[data-testid=account-menu]"));
        Assert.DoesNotContain("Old bank", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_account_sends_its_name_bank_and_owner()
    {
        var cut = Render("User");

        cut.Find("input[data-testid=new-account-name]").Input("Joint bills");
        cut.Find("input[data-testid=new-account-provider]").Change("Nationwide");
        cut.Find("[data-testid=add-account]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Joint bills", Assert.Single(_api.Added).Name));
        Assert.Equal(("Nationwide", (Guid?)null, (Guid?)null), (_api.Added[0].Provider, _api.Added[0].OwnerPersonId, _api.Added[0].ParentAccountId));
    }

    [Fact]
    public void A_pot_is_added_to_its_account()
    {
        var cut = Render("User");

        cut.FindAll("[data-testid=account]").Single(e => e.GetAttribute("aria-label") == "Joint Monzo").QuerySelector("[data-testid=add-pot]")!.Click();
        cut.Find("input[data-testid=new-pot-name]").Input("Garden");
        cut.Find("[data-testid=add-pot-save]").Click();

        cut.WaitForAssertion(() => Assert.Equal(("Garden", (Guid?)MonzoId), (Assert.Single(_api.Added).Name, _api.Added[0].ParentAccountId)));
    }

    [Fact]
    public void A_refusal_is_shown()
    {
        _api.Refusal = "There's already an account with that name.";
        var cut = Render("User");

        cut.Find("input[data-testid=new-account-name]").Input("Joint Monzo");
        cut.Find("[data-testid=add-account]").Click();

        cut.WaitForAssertion(() => Assert.Contains("already an account", cut.Find("[data-testid=account-error]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Archived_accounts_can_be_shown()
    {
        var cut = Render("User");

        cut.FindComponent<MudBlazor.MudSwitch<bool>>().Find("input").Change(true);

        Assert.Contains("Old bank", Texts(cut, "account-name"));
        Assert.Single(cut.FindAll("[data-testid=account-archived]"));
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Admins_turn_on_transfers_and_then_get_a_link_to_them()
    {
        var cut = Render("Admin");
        Assert.Empty(cut.FindAll("[data-testid=go-to-transfers]"));

        cut.FindComponent<MudBlazor.MudSwitch<bool>>().Find("input").Change(true);

        cut.WaitForAssertion(() => Assert.Equal("/transfers", cut.Find("[data-testid=go-to-transfers]").GetAttribute("href")));
        Assert.True(Assert.Single(_households.Saved).TransfersEnabled);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Others_only_see_transfers_once_they_are_on(bool enabled, int links)
    {
        var cut = Render("User", transfersEnabled: enabled);

        Assert.Empty(cut.FindAll("[data-testid=transfers-enabled]"));
        Assert.Equal(links, cut.FindAll("[data-testid=go-to-transfers]").Count);
    }

    private IRenderedComponent<AccountsPage> Render(string role, bool transfersEnabled = false)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner: false, transfersEnabled));
        return _context.RenderComponent<AccountsPage>();
    }

    private static string[] Texts(IRenderedComponent<AccountsPage> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => System.Text.RegularExpressions.Regex.Replace(e.TextContent, @"\s+", " ").Trim()).ToArray();

    private sealed class FakeAccountsApi : IAccountsApi
    {
        public List<SaveAccountRequest> Added { get; } = [];

        public string? Refusal { get; set; }

        public Task<IReadOnlyList<AccountSummary>> ListAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<AccountSummary>>(
            [
                new(ChrisCurrentId, "Chris current", null, StaticPeopleApi.ChrisId, "Chris Austin", null, false, "Chris current", 1),
                new(MonzoId, "Joint Monzo", "Monzo", null, null, null, false, "Joint Monzo", 3),
                new(Guid.NewGuid(), "Holiday", null, null, null, MonzoId, false, "Joint Monzo › Holiday", 2),
                new(Guid.NewGuid(), "Old bank", null, null, null, null, true, "Old bank", 0),
            ]);

        public async Task<AccountsResult> AddAsync(Guid householdId, SaveAccountRequest request)
        {
            Added.Add(request);
            return Refusal is null
                ? new AccountsResult(await ListAsync(householdId), new Dictionary<string, string[]>())
                : new AccountsResult(null, new Dictionary<string, string[]> { ["Name"] = [Refusal] });
        }

        public Task<AccountsResult> UpdateAsync(Guid householdId, Guid accountId, SaveAccountRequest request) => throw new NotSupportedException();

        public Task<AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived) => throw new NotSupportedException();

        public Task<AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId) => throw new NotSupportedException();
    }
}
