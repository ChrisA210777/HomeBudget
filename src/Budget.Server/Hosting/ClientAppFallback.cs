namespace Budget.Server.Hosting;

/// <summary>
/// Serves the Blazor app's index.html for client-side routes such as /income.
/// </summary>
/// <remarks>
/// The client's index.html has placeholders (e.g. the fingerprinted framework script name) that
/// the build fills in. The filled-in page is only available through its static-asset endpoint
/// (MapStaticAssets); the file on disk still has the placeholders. <c>MapFallbackToFile</c> serves the
/// file on disk, which works in Development but leaves a published app stuck on "Loading…".
/// So the fallback hands the request to the static-asset endpoint for index.html instead.
/// </remarks>
public static class ClientAppFallback
{
    private const string IndexHtml = "index.html";

    public static IEndpointConventionBuilder MapClientAppFallback(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Endpoint? indexEndpoint = null;
        return app.MapFallback(async context =>
            {
                indexEndpoint ??= FindIndexEndpoint(context.RequestServices.GetRequiredService<EndpointDataSource>());
                if (indexEndpoint?.RequestDelegate is { } serveIndex)
                {
                    context.SetEndpoint(indexEndpoint);
                    await serveIndex(context);
                    return;
                }

                // No static-asset endpoint (shouldn't happen): serve the file as it is.
                var file = app.Environment.WebRootFileProvider.GetFileInfo(IndexHtml);
                if (!file.Exists)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(file);
            })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
    }

    /// <summary>The uncompressed static-asset endpoint for index.html.</summary>
    private static RouteEndpoint? FindIndexEndpoint(EndpointDataSource endpoints) =>
        endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText?.TrimStart('/'), IndexHtml, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Metadata.Any(m => m.GetType().Name.Contains("ContentEncoding", StringComparison.Ordinal)) ? 1 : 0)
            .FirstOrDefault();
}
