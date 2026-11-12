using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Budget.Server.Features.DataExport;
using Budget.Server.Hosting;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.DataExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Budget.Server.Tests.Hosting;

/// <summary>Rate limits and request size limits (BUD-144).</summary>
[Collection(ApiCollection.Name)]
public class RequestLimitTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Exports_are_limited_per_person_and_the_limit_resets()
    {
        using var limited = WithLimits(("DataPerMinute", "2"), ("WindowSeconds", "2"));
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var (otherId, otherHouseholdId) = await CreateHouseholdOwnerAsync();
        using var owner = Client(limited, ownerId);
        using var other = Client(limited, otherId);
        var export = new Uri(DataRoutes.Export(householdId, false, false), UriKind.Relative);

        // Sent in quick succession, so all three fall in the same two-second window.
        using (var first = await owner.GetAsync(export))
        using (var second = await owner.GetAsync(export))
        using (var third = await owner.GetAsync(export))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
            Assert.True(third.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            using var problem = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
            Assert.Equal(RateLimits.TooManyRequestsTitle, problem.RootElement.GetProperty("title").GetString());
        }

        // Someone else has their own count.
        using (var theirs = await other.GetAsync(new Uri(DataRoutes.Export(otherHouseholdId, false, false), UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, theirs.StatusCode);
        }

        await Task.Delay(TimeSpan.FromSeconds(3));
        using var afterwards = await owner.GetAsync(export);
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
    }

    [Fact]
    public async Task The_api_as_a_whole_is_limited_per_person()
    {
        using var limited = WithLimits(("ApiPerMinute", "3"));
        var (ownerId, _) = await CreateHouseholdOwnerAsync();
        using var owner = Client(limited, ownerId);
        var me = new Uri(AccountRoutes.CurrentUser, UriKind.Relative);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await owner.GetAsync(me);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, statuses);

        // Health checks and the app's own files aren't counted.
        using var health = await owner.GetAsync(new Uri(ApiRoutes.Health, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Starting_a_sign_in_is_limited_per_address_and_says_to_wait()
    {
        using var limited = WithLimits(("SignInPerMinute", "2"));
        using var browser = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        browser.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        var login = new Uri(AccountRoutes.Login("Microsoft", "/"), UriKind.Relative);

        using (var first = await browser.GetAsync(login))
        using (var second = await browser.GetAsync(login))
        {
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        }

        using var third = await browser.GetAsync(login);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("text/plain", third.Content.Headers.ContentType?.MediaType);
        Assert.Equal(RateLimits.TooManyRequestsDetail, await third.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Large_requests_are_refused_before_they_are_read()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var owner = CreateClient(signedInAs: ownerId);
        var tooBig = new string('x', (int)RequestSizeLimits.DefaultMaxBytes + 1);
        using var content = new StringContent($"{{\"name\":\"{tooBig}\"}}", Encoding.UTF8, "application/json");

        using var response = await owner.PostAsync(new Uri($"/api/households/{householdId}/people", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Imports_may_send_up_to_the_file_limit_but_no_more()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var owner = CreateClient(signedInAs: ownerId);
        var preview = new Uri(DataRoutes.Preview(householdId), UriKind.Relative);

        // Bigger than the API's usual limit, but a file the import can check (and refuse, as it isn't an export).
        using (var file = Json(new byte[RequestSizeLimits.DefaultMaxBytes * 4]))
        using (var accepted = await owner.PostAsync(preview, file))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var huge = Json(new byte[DataExportEndpoints.MaxUploadBytes + 1]);
        using var refused = await owner.PostAsync(preview, huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
    }

    private WebApplicationFactory<Program> WithLimits(params (string Name, string Value)[] limits) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(limits.ToDictionary(
                l => $"{RateLimitOptions.SectionName}:{l.Name}", l => (string?)l.Value))));

    private static HttpClient Client(WebApplicationFactory<Program> app, Guid userId)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiRoutes.CsrfHeaderName, "1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static ByteArrayContent Json(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }
}
