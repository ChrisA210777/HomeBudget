using Budget.Server.Hosting;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;

namespace Budget.Server.Tests.Hosting;

/// <summary>Security headers and the Content Security Policy (BUD-143).</summary>
[Collection(ApiCollection.Name)]
public class SecurityHeadersTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Every_response_has_the_security_headers()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ApiRoutes.Health, UriKind.Relative));

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
        Assert.Equal("require-corp", Header(response, "Cross-Origin-Embedder-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Resource-Policy"));
        Assert.Equal(SecurityHeaders.PermissionsPolicy, Header(response, "Permissions-Policy"));
        // The test host runs as Development.
        Assert.Equal(SecurityHeaders.DevelopmentContentSecurityPolicy, Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task Error_responses_have_them_too()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/households/{Guid.NewGuid()}/activity", UriKind.Relative));

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.NotNull(Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task Server_responses_are_never_cached_but_the_app_files_can_be()
    {
        var (ownerId, _) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var user = await client.GetAsync(new Uri(AccountRoutes.CurrentUser, UriKind.Relative));
        using var app = await client.GetAsync(new Uri("/manifest.webmanifest", UriKind.Relative));

        Assert.Equal("no-store", Header(user, "Cache-Control"));
        Assert.Equal("require-corp", Header(app, "Cross-Origin-Embedder-Policy"));
        Assert.DoesNotContain("no-store", Header(app, "Cache-Control") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deployed_policy_allows_nothing_inline_or_from_elsewhere()
    {
        var directives = SecurityHeaders.ContentSecurityPolicy
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(d => d.Split(' ')[0], d => d);

        Assert.Equal("script-src 'self' 'wasm-unsafe-eval'", directives["script-src"]);
        Assert.Equal("connect-src 'self'", directives["connect-src"]);
        Assert.Equal("font-src 'self'", directives["font-src"]);
        Assert.Equal("frame-ancestors 'none'", directives["frame-ancestors"]);
        Assert.Equal("object-src 'none'", directives["object-src"]);
        Assert.DoesNotContain("http", SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;
}
