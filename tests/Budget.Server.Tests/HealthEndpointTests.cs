using System.Net;
using System.Text.Json;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.CashFlow;

namespace Budget.Server.Tests;

/// <summary>Health checks and correlation ids (BUD-12).</summary>
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

    [Fact]
    public async Task Ready_endpoint_checks_the_database()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ApiRoutes.Ready, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_response_has_a_correlation_id()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ApiRoutes.Health, UriKind.Relative));

        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(response.Headers.GetValues(ApiRoutes.CorrelationIdHeaderName))));
    }

    [Fact]
    public async Task Problem_details_carry_the_same_correlation_id()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        // Cash flow is turned off for a new household, which is a problem response.
        using var response = await client.GetAsync(new Uri(CashFlowRoutes.Month(householdId, 2026, 11), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            Assert.Single(response.Headers.GetValues(ApiRoutes.CorrelationIdHeaderName)),
            problem.RootElement.GetProperty("traceId").GetString());
    }
}
