using System.Net;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;

namespace Budget.Server.Tests;

[Collection(ApiCollection.Name)]
public class HealthEndpointTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ApiRoutes.Health, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
