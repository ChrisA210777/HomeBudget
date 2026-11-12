namespace Budget.Server.Hosting;

/// <summary>
/// Security headers on every response (BUD-143, OWASP review BUD-74). The Content Security Policy is as tight as Blazor
/// WebAssembly and MudBlazor allow: everything comes from the app itself, scripts may compile WebAssembly but nothing is
/// inline, and the app can't be framed. MudBlazor sets inline styles, so styles allow 'unsafe-inline'.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>The policy in deployed environments and the end-to-end tests.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'wasm-unsafe-eval'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self'; "
        + "manifest-src 'self'; "
        + "worker-src 'self'; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'";

    /// <summary>
    /// In Development, hot reload and the WebAssembly debugger add scripts and connect over websockets, so those are
    /// allowed there and nowhere else.
    /// </summary>
    public const string DevelopmentContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'wasm-unsafe-eval' 'unsafe-inline'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self' ws: wss: http://localhost:* https://localhost:*; "
        + "manifest-src 'self'; "
        + "worker-src 'self'; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'";

    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=(), "
        + "web-share=(self), clipboard-write=(self)";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(environment);

        var policy = environment.IsDevelopment() ? DevelopmentContentSecurityPolicy : ContentSecurityPolicy;
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = policy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                // Everything the app loads is its own, so it can refuse anything another site hasn't opted in to
                // sharing (BUD-145). With the opener policy, this isolates the app from other sites' windows.
                headers["Cross-Origin-Embedder-Policy"] = "require-corp";

                // Responses from the server hold household data, so they're never kept in a browser's or proxy's
                // cache (BUD-145). The app's own files keep the caching MapStaticAssets gives them.
                if (IsServerResponse(context.Request.Path))
                {
                    headers.CacheControl = "no-store";
                    headers.Pragma = "no-cache";
                }
                return Task.CompletedTask;
            });

            await next(context);
        });
    }

    private static bool IsServerResponse(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase);
}
