using System.Net;
using System.Net.Http.Json;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Server.Tests.Account;

[Collection(ApiCollection.Name)]
public class CurrentUserTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Uri CurrentUser = new(AccountRoutes.CurrentUser, UriKind.Relative);

    [Fact]
    public async Task Returns_401_when_not_signed_in()
    {
        using var client = CreateClient(followRedirects: false);

        using var response = await client.GetAsync(CurrentUser);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_the_signed_in_user()
    {
        var user = await CreateUserAsync("Chris Austin", "chris@example.com");
        using var client = CreateClient(signedInAs: user.Id);

        var response = await client.GetFromJsonAsync<CurrentUserResponse>(CurrentUser);

        Assert.NotNull(response);
        Assert.Equal(user.Id, response.Id);
        Assert.Equal("Chris Austin", response.DisplayName);
        Assert.Equal("chris@example.com", response.Email);
        Assert.Empty(response.Households);
    }

    [Fact]
    public async Task Returns_401_when_the_signed_in_user_no_longer_exists()
    {
        using var client = CreateClient(signedInAs: Guid.NewGuid(), followRedirects: false);

        using var response = await client.GetAsync(CurrentUser);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
