using Budget.Client.Account;
using Budget.Client.Layout;
using Budget.Shared.Account;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Layout;

public sealed class UserMenuTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly RecordingAccountApi _accountApi = new();

    public UserMenuTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountApi>(_accountApi);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    private FakeNavigationManager Navigation => _context.Services.GetRequiredService<FakeNavigationManager>();

    [Fact]
    public void Shows_the_signed_in_users_name()
    {
        var cut = _context.RenderComponent<UserMenu>();

        Assert.Equal("Chris Austin", cut.Find("[data-testid=user-menu-button]").TextContent.Trim());
    }

    [Fact]
    public void Signing_out_clears_the_session_and_reloads_on_the_sign_in_page()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = _context.RenderComponent<UserMenu>();

        cut.Find("[data-testid=user-menu-button]").Click();
        popovers.WaitForElement("[data-testid=sign-out]").Click();

        Assert.True(_accountApi.SignedOut);
        Assert.Equal("http://localhost/login?signedOut=true", Navigation.Uri);
        Assert.True(Navigation.History.First().Options.ForceLoad);
    }

    [Fact]
    public void Stays_put_and_says_so_if_signing_out_fails()
    {
        _accountApi.SignOutError = new HttpRequestException("offline");
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var snackbars = _context.RenderComponent<MudSnackbarProvider>();
        var cut = _context.RenderComponent<UserMenu>();

        cut.Find("[data-testid=user-menu-button]").Click();
        popovers.WaitForElement("[data-testid=sign-out]").Click();

        Assert.Equal("http://localhost/", Navigation.Uri);
        snackbars.WaitForAssertion(() => Assert.Contains("Couldn't sign out", snackbars.Markup, StringComparison.Ordinal));
    }

    public void Dispose() => _context.Dispose();

    private sealed class RecordingAccountApi : IAccountApi
    {
        public bool SignedOut { get; private set; }

        public Exception? SignOutError { get; set; }

        public Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthProvider>>([]);

        public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentUserResponse?>(null);

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            if (SignOutError is not null)
            {
                return Task.FromException(SignOutError);
            }

            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
