using Budget.Client.Account;
using Budget.Client.Budgeting;
using Budget.Client.Households;
using Budget.Client.Outgoings;
using Budget.Client.Pages;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages;

public sealed class HomeTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly StubAccountApi _accountApi = new();
    private readonly FakeHouseholdApi _householdApi = new();
    private readonly FakeBudgetApi _budgetApi = new();
    private readonly FakeRenewalApi _renewalApi = new();

    public HomeTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountApi>(_accountApi);
        _context.Services.AddSingleton<IHouseholdApi>(_householdApi);
        _context.Services.AddSingleton<IBudgetApi>(_budgetApi);
        _context.Services.AddSingleton<IRenewalApi>(_renewalApi);
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void The_budget_card_shows_whats_left_this_month()
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "My Household", "Admin", IsOwner: true));

        var cut = _context.RenderComponent<Home>();

        Assert.Equal("£1,290.00", cut.Find("[data-testid=home-left]").TextContent.Trim());
        Assert.Contains("£3,000.00 in, £1,710.00 out", cut.Find("[data-testid=budget-card]").TextContent, StringComparison.Ordinal);
        Assert.Equal((null, null), _budgetApi.Requests.Single());
    }

    [Fact]
    public void Renewals_coming_up_are_listed_soonest_first_with_what_to_do()
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "My Household", "Viewer", IsOwner: false));
        _renewalApi.ComingUp =
        [
            new(Guid.NewGuid(), "Car insurance", new DateOnly(2027, 3, 3), 12, AutoRenews: true, 480m, 1, PaymentUnitDto.Year, "GBP"),
            new(Guid.NewGuid(), "TV Licence", new DateOnly(2027, 3, 20), 29, AutoRenews: false, 174.50m, 1, PaymentUnitDto.Year, "GBP"),
        ];

        var cut = _context.RenderComponent<Home>();

        var texts = cut.FindAll("[data-testid=coming-up-text]").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal("Renews automatically on 3 Mar 2027. Switch or cancel before then if you want to.", texts[0]);
        Assert.Equal("Needs renewing by 20 Mar 2027.", texts[1]);
        Assert.Equal(new[] { "Renews in 12 days", "Renews in 29 days" }, cut.FindAll("[data-testid=coming-up-days]").Select(e => e.TextContent.Trim()));
    }

    [Fact]
    public void There_is_no_coming_up_card_when_nothing_is_due()
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "My Household", "Admin", IsOwner: true));

        var cut = _context.RenderComponent<Home>();

        Assert.Empty(cut.FindAll("[data-testid=coming-up-card]"));
    }

    [Fact]
    public void A_shortfall_on_the_budget_card_says_so_in_words()
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "My Household", "Admin", IsOwner: true));
        _budgetApi.Month = FakeBudgetApi.Sample(2026, 11, income: 1000m, essential: 1200m, preferred: 0m, optional: 0m);

        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Short by £200.00", cut.Find("[data-testid=home-left]").TextContent.Trim());
    }

    [Fact]
    public void Shows_the_active_household_as_the_heading()
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "My Household", "Admin", IsOwner: true));

        var cut = _context.RenderComponent<Home>();

        Assert.Equal("My Household", cut.Find("h1").TextContent.Trim());
        Assert.Equal("You're the owner of this household.", cut.Find("[data-testid=household-role]").TextContent.Trim());
    }

    [Fact]
    public void Shows_the_role_for_members_who_are_not_the_owner()
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "Smith Family", "Viewer", IsOwner: false));

        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Your role in this household: Viewer.", cut.Find("[data-testid=household-role]").TextContent.Trim());
    }

    [Fact]
    public void Welcomes_the_signed_in_user_by_name()
    {
        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Welcome, Chris Austin", cut.Find("[data-testid=welcome]").TextContent.Trim());
    }

    [Fact]
    public void Falls_back_to_the_app_name_without_a_household()
    {
        _accountApi.User = new CurrentUserResponse(Guid.NewGuid(), "Chris Austin", null, [], null);

        var cut = _context.RenderComponent<Home>();

        Assert.Equal("Home Budget", cut.Find("h1").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=household-role]"));
    }

    [Fact]
    public void A_new_household_is_prompted_to_add_people_income_and_outgoings_in_that_order()
    {
        UseHousehold("Admin");
        _householdApi.Setup = new HouseholdSetupResponse(0, 0, 1);

        var cut = _context.RenderComponent<Home>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=getting-started]")));
        var steps = cut.FindAll("[data-testid=getting-started] li").Select(li => li.GetAttribute("data-testid")).ToArray();
        Assert.Equal(new[] { "setup-add-people", "setup-add-income", "setup-add-outgoing" }, steps);
        Assert.Equal("/income/new?from=%2F", cut.Find("[data-testid=setup-add-income-go]").GetAttribute("href"));
        Assert.Equal("/outgoings/new?from=%2F", cut.Find("[data-testid=setup-add-outgoing-go]").GetAttribute("href"));
        Assert.Equal("/people", cut.Find("[data-testid=setup-add-people-go]").GetAttribute("href"));
    }

    [Fact]
    public void Only_the_steps_not_done_are_shown()
    {
        UseHousehold("User");
        _householdApi.Setup = new HouseholdSetupResponse(2, 0, 3);

        var cut = _context.RenderComponent<Home>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=setup-add-outgoing]")));
        Assert.Empty(cut.FindAll("[data-testid=setup-add-income]"));
        Assert.Empty(cut.FindAll("[data-testid=setup-add-people]"));
    }

    [Fact]
    public void Theres_no_getting_started_card_once_everything_is_set_up()
    {
        UseHousehold("Admin");
        _householdApi.Setup = new HouseholdSetupResponse(1, 1, 2);

        var cut = _context.RenderComponent<Home>();

        cut.WaitForAssertion(() => Assert.Equal(1, _householdApi.SetupCalls));
        Assert.Empty(cut.FindAll("[data-testid=getting-started]"));
        Assert.NotNull(cut.Find("[data-testid=budget-card]"));
    }

    [Fact]
    public void Viewers_get_no_prompts_because_they_cant_add_anything()
    {
        UseHousehold("Viewer");
        _householdApi.Setup = new HouseholdSetupResponse(0, 0, 1);

        var cut = _context.RenderComponent<Home>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=budget-card]")));
        Assert.Empty(cut.FindAll("[data-testid=getting-started]"));
        Assert.Equal(0, _householdApi.SetupCalls);
    }

    [Fact]
    public void Not_now_hides_the_people_step_and_is_remembered_on_this_device()
    {
        UseHousehold("Admin");
        _householdApi.Setup = new HouseholdSetupResponse(1, 1, 1);

        var cut = _context.RenderComponent<Home>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=setup-people-not-now]").Click());

        Assert.Empty(cut.FindAll("[data-testid=getting-started]"));
        Assert.Contains(_context.JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem" && i.Arguments[1] as string == "true");
    }

    [Fact]
    public void The_menu_cards_are_gone()
    {
        UseHousehold("Admin");

        var cut = _context.RenderComponent<Home>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=budget-card]")));
        Assert.Empty(cut.FindAll("[data-testid=income-card], [data-testid=people-card], [data-testid=outgoings-card]"));
    }

    [Fact]
    public void Admins_can_rename_the_household_in_place()
    {
        var householdId = UseHousehold("Admin");
        var cut = _context.RenderComponent<Home>();

        cut.Find("[data-testid=rename-household]").Click();
        Assert.Equal("My Household", cut.Find("input[data-testid=rename-input]").GetAttribute("value"));
        cut.Find("input[data-testid=rename-input]").Input("Smith Family");
        cut.Find("[data-testid=rename-save]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Smith Family", cut.Find("h1").TextContent.Trim()));
        var (id, request) = Assert.Single(_householdApi.Renames);
        Assert.Equal(householdId, id);
        Assert.Equal("Smith Family", request.Name);
        Assert.Equal("My Household", request.PreviousName);
    }

    [Fact]
    public void Enter_saves_and_Escape_cancels()
    {
        UseHousehold("Admin");
        var cut = _context.RenderComponent<Home>();

        cut.Find("[data-testid=rename-household]").Click();
        cut.Find("input[data-testid=rename-input]").Input("Smiths");
        cut.Find("input[data-testid=rename-input]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal("My Household", cut.Find("h1").TextContent.Trim());
        Assert.Empty(_householdApi.Renames);

        cut.Find("[data-testid=rename-household]").Click();
        cut.Find("input[data-testid=rename-input]").Input("Smiths");
        cut.Find("input[data-testid=rename-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("Smiths", cut.Find("h1").TextContent.Trim()));
    }

    [Fact]
    public void A_blank_name_is_rejected_before_anything_is_sent()
    {
        UseHousehold("Admin");
        var cut = _context.RenderComponent<Home>();

        cut.Find("[data-testid=rename-household]").Click();
        cut.Find("input[data-testid=rename-input]").Input("  ");
        cut.Find("[data-testid=rename-save]").Click();

        Assert.Empty(_householdApi.Renames);
        Assert.Contains("Enter a name for the household.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Someone_elses_rename_is_shown_rather_than_overwritten()
    {
        UseHousehold("Admin");
        _householdApi.ConflictName = "The Smiths";
        var cut = _context.RenderComponent<Home>();

        cut.Find("[data-testid=rename-household]").Click();
        cut.Find("input[data-testid=rename-input]").Input("Smith Family");
        cut.Find("[data-testid=rename-save]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("The Smiths", cut.Find("h1").TextContent.Trim());
            Assert.Contains("The Smiths", cut.Find("[data-testid=rename-conflict]").TextContent, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Viewer")]
    public void Only_admins_see_the_rename_action(string role)
    {
        UseHousehold(role, isOwner: false);

        var cut = _context.RenderComponent<Home>();

        Assert.Empty(cut.FindAll("[data-testid=rename-household]").ToList());
    }

    public void Dispose() => _context.Dispose();

    private Guid UseHousehold(string role, bool isOwner = true)
    {
        var householdId = Guid.NewGuid();
        _accountApi.User = UserWith(householdId, new HouseholdMembershipSummary(householdId, "My Household", role, isOwner));
        return householdId;
    }

    [Fact]
    public void Someone_with_no_household_is_offered_to_create_one()
    {
        _accountApi.User = new CurrentUserResponse(Guid.NewGuid(), "Chris Austin", "chris@outlook.com", [], null);

        var cut = _context.RenderComponent<Home>();

        cut.WaitForAssertion(() => Assert.Equal("/households/new", cut.Find("[data-testid=no-household-create]").GetAttribute("href")));
        Assert.Empty(cut.FindAll("[data-testid=budget-card]"));
    }

    private static CurrentUserResponse UserWith(Guid activeHouseholdId, HouseholdMembershipSummary household) =>
        new(Guid.NewGuid(), "Chris Austin", "chris@outlook.com", [household], activeHouseholdId);

    private sealed class FakeHouseholdApi : IHouseholdApi
    {
        public List<(Guid Id, RenameHouseholdRequest Request)> Renames { get; } = [];

        public string? ConflictName { get; set; }

        public Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId) =>
            Task.FromResult<IReadOnlyList<HouseholdMemberSummary>>([]);

        public HouseholdSetupResponse Setup { get; set; } = new(1, 1, 2);

        public int SetupCalls { get; private set; }

        public Task<HouseholdSetupResponse> GetSetupAsync(Guid householdId)
        {
            SetupCalls++;
            return Task.FromResult(Setup);
        }

        public Task<string?> ChangeRoleAsync(Guid householdId, Guid userId, string role) => throw new NotSupportedException();

        public Task<string?> RemoveMemberAsync(Guid householdId, Guid userId) => throw new NotSupportedException();

        public Task<string?> LeaveAsync(Guid householdId) => throw new NotSupportedException();

        public Task DeleteAsync(Guid householdId) => throw new NotSupportedException();

        public Task<CreateHouseholdResult> CreateAsync(CreateHouseholdRequest request) => throw new NotSupportedException();

        public Task<bool> SwitchToAsync(Guid householdId) => throw new NotSupportedException();

        public Task<HouseholdResponse> SaveSettingsAsync(Guid householdId, HouseholdSettingsRequest request) => throw new NotSupportedException();

        public Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request)
        {
            Renames.Add((householdId, request));
            var noErrors = new Dictionary<string, string[]>();
            return Task.FromResult(ConflictName is { } current
                ? new RenameHouseholdResult(null, noErrors, current)
                : new RenameHouseholdResult(new HouseholdResponse(householdId, request.Name.Trim(), "GBP", "Admin", true, 1), noErrors));
        }
    }

    private sealed class StubAccountApi : IAccountApi
    {
        public CurrentUserResponse? User { get; set; }

        public Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthProvider>>([]);

        public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(User);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
