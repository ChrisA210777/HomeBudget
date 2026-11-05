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

        // The build fills in the asset placeholders; serving the raw file leaves the app stuck loading.
        Assert.DoesNotContain("#[", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script type=\"importmap\"></script>", html, StringComparison.Ordinal);
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

    private static async Task<string> GetPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("<script src=\"(?<src>_framework/blazor\\.webassembly[^\"]*\\.js)\"")]
    private static partial Regex FrameworkScript();
}
