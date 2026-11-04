using System.Net;
using System.Net.Http.Json;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Server.Tests.Account;

[Collection(ApiCollection.Name)]
public class SignInTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Providers_lists_Microsoft_when_it_is_configured()
    {
        using var client = CreateClient();

        var providers = await client.GetFromJsonAsync<List<AuthProvider>>(new Uri(AccountRoutes.Providers, UriKind.Relative));

        var microsoft = Assert.Single(providers!);
        Assert.Equal("Microsoft", microsoft.Name);
        Assert.Equal("Microsoft", microsoft.DisplayName);
    }

    [Fact]
    public async Task Login_sends_the_browser_to_Microsoft_for_personal_and_work_accounts()
    {
        using var client = CreateClient(followRedirects: false);

        using var response = await client.GetAsync(new Uri(AccountRoutes.Login("Microsoft", "/income"), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();

        // The "common" endpoint accepts both personal Microsoft accounts and work or school accounts.
        Assert.StartsWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", location, StringComparison.Ordinal);
        Assert.Contains($"client_id={BudgetApiFactory.MicrosoftClientId}", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/signin-microsoft"), location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_with_an_unknown_provider_returns_not_found()
    {
        using var client = CreateClient(followRedirects: false);

        using var response = await client.GetAsync(new Uri(AccountRoutes.Login("NotAProvider", "/"), UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task External_callback_without_a_provider_sign_in_goes_back_to_the_sign_in_page()
    {
        using var client = CreateClient(followRedirects: false);

        using var response = await client.GetAsync(new Uri(AccountRoutes.ExternalCallback, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(AccountRoutes.LoginPageWithError(LoginErrors.Failed), response.Headers.Location!.ToString());
    }
}
