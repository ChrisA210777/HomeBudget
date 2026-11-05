using System.Net;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;

namespace Budget.Server.Tests.Hosting;

[Collection(ApiCollection.Name)]
public class ForwardedHeadersTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Behind_a_dev_tunnel_Microsoft_sends_you_back_to_the_tunnel()
    {
        var location = await LoginRedirectAsync("abc123-7180.uks1.devtunnels.ms");

        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString("https://abc123-7180.uks1.devtunnels.ms/signin-microsoft"),
            location,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_forwarded_host_that_is_not_a_tunnel_is_ignored()
    {
        var location = await LoginRedirectAsync("evil.example.com");

        Assert.DoesNotContain("evil.example.com", location, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> LoginRedirectAsync(string forwardedHost)
    {
        using var client = CreateClient(followRedirects: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AccountRoutes.Login("Microsoft", "/"), UriKind.Relative));
        request.Headers.Add("X-Forwarded-Host", forwardedHost);
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }
}
