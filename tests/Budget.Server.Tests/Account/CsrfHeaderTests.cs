using System.Net;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;

namespace Budget.Server.Tests.Account;

[Collection(ApiCollection.Name)]
public class CsrfHeaderTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Uri BffEndpoint = new(AccountRoutes.CurrentUser, UriKind.Relative);

    [Fact]
    public async Task State_changing_request_without_the_header_is_rejected()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);
        client.DefaultRequestHeaders.Remove(ApiRoutes.CsrfHeaderName);

        using var response = await client.PostAsync(BffEndpoint, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task State_changing_request_with_the_header_gets_past_the_check()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);

        using var response = await client.PostAsync(BffEndpoint, content: null);

        // There's no POST endpoint here, so once past the check the request falls through to the
        // "unknown /bff route" 404 rather than being rejected as a forgery.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_request_does_not_need_the_header()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);
        client.DefaultRequestHeaders.Remove(ApiRoutes.CsrfHeaderName);

        using var response = await client.GetAsync(BffEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
