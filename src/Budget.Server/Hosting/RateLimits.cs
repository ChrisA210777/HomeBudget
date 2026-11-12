using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Budget.Server.Hosting;

/// <summary>
/// How many requests a caller can make in a minute (BUD-144, OWASP review BUD-74). Set under <c>RateLimits</c> in
/// configuration; the defaults are generous for a person using the app and stop scripts hammering it.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>The API in general, per signed-in person (or per address when signed out).</summary>
    public int ApiPerMinute { get; set; } = 600;

    /// <summary>Starting a sign-in and finishing it, per address.</summary>
    public int SignInPerMinute { get; set; } = 20;

    /// <summary>Looking up and accepting invitations, per person, so invitation links can't be guessed.</summary>
    public int InvitationsPerMinute { get; set; } = 10;

    /// <summary>Downloading and importing a household's data, per person.</summary>
    public int DataPerMinute { get; set; } = 10;

    /// <summary>How long each count lasts. A minute, except in tests that wait for a limit to reset.</summary>
    public int WindowSeconds { get; set; } = 60;
}

public static class RateLimits
{
    /// <summary>Policy for starting and finishing a sign-in.</summary>
    public const string SignIn = "sign-in";

    /// <summary>Policy for looking up and accepting invitations.</summary>
    public const string Invitations = "invitations";

    /// <summary>Policy for exports and imports.</summary>
    public const string Data = "data";

    public const string TooManyRequestsTitle = "Too many requests";
    public const string TooManyRequestsDetail = "There have been a lot of requests in a short time. Wait a moment, then try again.";

    public static IServiceCollection AddBudgetRateLimits(this IServiceCollection services)
    {
        // Read when a limit is first needed rather than now, so settings added later (e.g. by tests) apply.
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RejectAsync;

            // Every API call counts towards the general limit; the policies below add tighter ones for sensitive actions.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsApi(context.Request.Path)
                    ? RateLimitPartition.GetFixedWindowLimiter("api:" + Caller(context), _ => Window(Limits(context), l => l.ApiPerMinute))
                    : RateLimitPartition.GetNoLimiter(string.Empty));

            options.AddPolicy(SignIn, context =>
                RateLimitPartition.GetFixedWindowLimiter("ip:" + Address(context), _ => Window(Limits(context), l => l.SignInPerMinute)));
            options.AddPolicy(Invitations, context =>
                RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => Window(Limits(context), l => l.InvitationsPerMinute)));
            options.AddPolicy(Data, context =>
                RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => Window(Limits(context), l => l.DataPerMinute)));
        });

        return services;
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static bool IsApi(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase);

    /// <summary>The signed-in person, or the address a signed-out request came from.</summary>
    private static string Caller(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } user ? "user:" + user : "ip:" + Address(context);

    // The forwarded headers middleware runs first, so behind App Service's front end this is the person's address.
    private static string Address(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(RateLimitOptions limits, Func<RateLimitOptions, int> permits) => new()
    {
        PermitLimit = permits(limits),
        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static async ValueTask RejectAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(1);
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = TooManyRequestsTitle,
            Detail = TooManyRequestsDetail,
        };
        // A browser going to a sign-in page shows the response, so it gets a sentence rather than JSON.
        var isPage = context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        var written = !isPage && await context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
        if (!written)
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(TooManyRequestsDetail, cancellationToken);
        }
    }
}
