using System.Net;
using System.Text.RegularExpressions;
using Budget.Server.Tests.Infrastructure;

namespace Budget.Server.Tests;

/// <summary>
/// Checks that the server hosts the Blazor WebAssembly client: the shell page loads and
/// the framework script it references can be downloaded.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class ClientHostingTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/some/client/route")]
    public async Task Client_routes_return_the_app_shell(string path)
    {
        using var client = CreateClient();

        var html = await GetPageAsync(client, path);

        Assert.Contains("<div id=\"app\">", html, StringComparison.Ordinal);

        // Build-time placeholders aren't filled in when the server hosts the client, and a page
        // that still has them never starts (it requests "_framework/blazor.webassembly").
        Assert.DoesNotContain("#[", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/bff/does-not-exist")]
    [InlineData("/account/does-not-exist")]
    public async Task Unknown_api_routes_return_not_found_rather_than_the_app_shell(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Framework_script_referenced_by_the_shell_is_served()
    {
        using var client = CreateClient();
        var html = await GetPageAsync(client, "/");

        var match = FrameworkScript().Match(html);
        Assert.True(match.Success, "index.html should reference the Blazor WebAssembly script.");

        using var response = await client.GetAsync(new Uri("/" + match.Groups["src"].Value, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_shell_has_no_inline_scripts_or_third_party_resources()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        // The Content Security Policy (BUD-143) allows only the app's own scripts, styles and fonts.
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.DoesNotMatch(InlineScript(), html);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
    }

    private static async Task<string> GetPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("<script(?![^>]*\\ssrc=)[^>]*>")]
    private static partial Regex InlineScript();

    [GeneratedRegex("<script src=\"(?<src>_framework/blazor\\.webassembly[^\"]*\\.js)\"")]
    private static partial Regex FrameworkScript();
}
