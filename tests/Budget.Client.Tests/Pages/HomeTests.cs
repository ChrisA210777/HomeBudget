using Budget.Client.Account;
using Budget.Client.Households;
using Budget.Client.Pages;
using Budget.Shared.Account;
using Budget.Shared.Households;
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

    public HomeTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountApi>(_accountApi);
        _context.Services.AddSingleton<IHouseholdApi>(_householdApi);
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
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
    public void Links_to_income_and_people()
    {
        var cut = _context.RenderComponent<Home>();

        Assert.Equal("/income", cut.Find("[data-testid=go-to-income]").GetAttribute("href"));
        Assert.Equal("/people", cut.Find("[data-testid=go-to-people]").GetAttribute("href"));
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

    private static CurrentUserResponse UserWith(Guid activeHouseholdId, HouseholdMembershipSummary household) =>
        new(Guid.NewGuid(), "Chris Austin", "chris@outlook.com", [household], activeHouseholdId);

    private sealed class FakeHouseholdApi : IHouseholdApi
    {
        public List<(Guid Id, RenameHouseholdRequest Request)> Renames { get; } = [];

        public string? ConflictName { get; set; }

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
