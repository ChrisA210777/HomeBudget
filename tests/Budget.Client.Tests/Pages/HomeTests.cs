using Budget.Client.Account;
using Budget.Client.Pages;
using Budget.Shared.Account;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages;

public sealed class HomeTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly StubAccountApi _accountApi = new();

    public HomeTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountApi>(_accountApi);
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

    public void Dispose() => _context.Dispose();

    private static CurrentUserResponse UserWith(Guid activeHouseholdId, HouseholdMembershipSummary household) =>
        new(Guid.NewGuid(), "Chris Austin", "chris@outlook.com", [household], activeHouseholdId);

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
