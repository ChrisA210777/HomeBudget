using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Budget.Server.Hosting;

/// <summary>
/// Keeps request bodies small (BUD-144): the API's requests are a few kilobytes of JSON, so anything over
/// <see cref="DefaultMaxBytes"/> is refused with 413 before it's read. Imports allow more with
/// <see cref="WithMaxRequestSize"/>.
/// </summary>
public static class RequestSizeLimits
{
    public const long DefaultMaxBytes = 256 * 1024;

    public static RouteHandlerBuilder WithMaxRequestSize(this RouteHandlerBuilder builder, long bytes) =>
        builder.WithMetadata(new MaxRequestSize(bytes));

    /// <summary>Call after routing has chosen the endpoint, so its own limit is known.</summary>
    public static IApplicationBuilder UseRequestSizeLimits(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize ?? DefaultMaxBytes;

            // Kestrel enforces the limit as the body is read, which covers bodies sent without a length.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = limit;
            }

            if (context.Request.ContentLength > limit)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status413PayloadTooLarge,
                        Title = "Request too large",
                        Detail = "That's more than the app accepts in one go.",
                    },
                });
                return;
            }

            await next(context);
        });

    private sealed record MaxRequestSize(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;
}
