using Budget.Client.Account;
using Budget.Client.Pages;
using Budget.Shared.Account;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages;

public sealed class LoginTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakeAccountApi _accountApi = new();

    public LoginTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountApi>(_accountApi);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private FakeNavigationManager Navigation => _context.Services.GetRequiredService<FakeNavigationManager>();

    [Fact]
    public void Shows_a_sign_in_button_for_each_provider()
    {
        _accountApi.Providers = [new AuthProvider("Microsoft", "Microsoft")];

        var cut = _context.RenderComponent<Login>();

        var button = Assert.Single(cut.FindAll("button"));
        Assert.Equal("Sign in with Microsoft", button.TextContent.Trim());
    }

    [Fact]
    public void Signing_in_goes_to_the_server_with_the_return_url_as_a_full_page_load()
    {
        _accountApi.Providers = [new AuthProvider("Microsoft", "Microsoft")];
        Navigation.NavigateTo("/login?returnUrl=%2Fincome");

        var cut = _context.RenderComponent<Login>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/account/login/Microsoft?returnUrl=%2Fincome", Navigation.Uri);
        Assert.True(Navigation.History.First().Options.ForceLoad);
    }

    [Fact]
    public void Ignores_a_return_url_that_points_to_another_site()
    {
        _accountApi.Providers = [new AuthProvider("Microsoft", "Microsoft")];
        Navigation.NavigateTo("/login?returnUrl=%2F%2Fevil.example.com");

        var cut = _context.RenderComponent<Login>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/account/login/Microsoft?returnUrl=%2F", Navigation.Uri);
    }

    [Theory]
    [InlineData("cancelled", "Sign-in was cancelled.")]
    [InlineData("failed", "Sign-in didn't work. Please try again.")]
    public void Explains_why_sign_in_did_not_complete(string error, string expectedMessage)
    {
        Navigation.NavigateTo($"/login?error={error}");

        var cut = _context.RenderComponent<Login>();

        Assert.Equal(expectedMessage, cut.Find("[data-testid=login-error]").TextContent.Trim());
    }

    [Fact]
    public void Confirms_when_the_user_has_signed_out()
    {
        Navigation.NavigateTo("/login?signedOut=true");

        var cut = _context.RenderComponent<Login>();

        Assert.Equal("You've signed out.", cut.Find("[data-testid=signed-out]").TextContent.Trim());
    }

    [Fact]
    public void Says_so_when_no_providers_are_configured()
    {
        _accountApi.Providers = [];

        var cut = _context.RenderComponent<Login>();

        Assert.Empty(cut.FindAll("button"));
        Assert.NotNull(cut.Find("[data-testid=no-providers]"));
    }

    public void Dispose() => _context.Dispose();

    private sealed class FakeAccountApi : IAccountApi
    {
        public IReadOnlyList<AuthProvider> Providers { get; set; } = [];

        public Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Providers);

        public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentUserResponse?>(null);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
