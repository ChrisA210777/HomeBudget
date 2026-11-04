using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Budget.Server.Tests;

/// <summary>
/// Checks that the server hosts the Blazor WebAssembly client: the shell page loads and
/// the framework script it references can be downloaded.
/// </summary>
public partial class ClientHostingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/some/client/route")]
    public async Task Client_routes_return_the_app_shell(string path)
    {
        using var client = factory.CreateClient();

        var html = await GetPageAsync(client, path);

        Assert.Contains("<div id=\"app\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Framework_script_referenced_by_the_shell_is_served()
    {
        using var client = factory.CreateClient();
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
