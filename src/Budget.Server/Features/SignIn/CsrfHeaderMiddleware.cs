using Budget.Shared;

namespace Budget.Server.Features.SignIn;

public static class CsrfHeaderMiddleware
{
    /// <summary>
    /// Rejects state-changing requests to the API that lack the <see cref="ApiRoutes.CsrfHeaderName"/>
    /// header. Because sign-in uses a cookie, this stops other sites from making requests on a
    /// signed-in user's behalf (cross-site request forgery).
    /// </summary>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (RequiresCheck(context.Request) && !context.Request.Headers.ContainsKey(ApiRoutes.CsrfHeaderName))
            {
                await Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Missing anti-forgery header",
                        detail: $"Requests that change data must include the '{ApiRoutes.CsrfHeaderName}' header.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    private static bool RequiresCheck(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method)
          || HttpMethods.IsHead(request.Method)
          || HttpMethods.IsOptions(request.Method)
          || HttpMethods.IsTrace(request.Method))
        && AuthenticationSetup.IsApiRequest(request);
}
