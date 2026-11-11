using System.Diagnostics;
using Budget.Shared;

namespace Budget.Server.Hosting;

/// <summary>
/// Gives every response a correlation id (BUD-12): the trace id Application Insights records as the operation id, or
/// ASP.NET Core's request id when tracing is off. Problem details carry the same id as <c>traceId</c>.
/// </summary>
public static class CorrelationId
{
    public static string For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Activity.Current is { } activity && activity.TraceId != default
            ? activity.TraceId.ToHexString()
            : context.TraceIdentifier;
    }

    public static IApplicationBuilder UseCorrelationIdHeader(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var id = For(context);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[ApiRoutes.CorrelationIdHeaderName] = id;
                return Task.CompletedTask;
            });
            return next(context);
        });

    public static IServiceCollection AddProblemDetailsWithCorrelationId(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = problem =>
            problem.ProblemDetails.Extensions["traceId"] = For(problem.HttpContext));
}
