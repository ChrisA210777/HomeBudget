using System.Net;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;

namespace Budget.Server.Tests.Account;

[Collection(ApiCollection.Name)]
public class SignOutTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Uri Logout = new(AccountRoutes.Logout, UriKind.Relative);

    [Fact]
    public async Task Signing_out_clears_the_session_cookie()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);

        using var response = await client.PostAsync(Logout, content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var session = Assert.Single(cookies, c => c.StartsWith("__Host-budget=", StringComparison.Ordinal));

        // An empty value with an expiry in the past tells the browser to delete the cookie.
        Assert.StartsWith("__Host-budget=;", session, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", session, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_out_when_already_signed_out_still_succeeds()
    {
        using var client = CreateClient();

        using var response = await client.PostAsync(Logout, content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_needs_the_anti_forgery_header()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);
        client.DefaultRequestHeaders.Remove(ApiRoutes.CsrfHeaderName);

        using var response = await client.PostAsync(Logout, content: null);

        // Otherwise another site could sign people out of the app.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
