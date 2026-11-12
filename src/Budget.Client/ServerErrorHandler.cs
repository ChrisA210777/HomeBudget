using System.Net;
using Budget.Shared;

namespace Budget.Client;

/// <summary>
/// Turns a server error (5xx) into a <see cref="ServerErrorException"/> carrying the response's correlation id, so the
/// page can show it and the problem can be found in Application Insights (BUD-12). Too many requests (429, BUD-144)
/// becomes a <see cref="TooManyRequestsException"/>, so pages can say to wait rather than that something broke.
/// </summary>
public sealed class ServerErrorHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            response.Dispose();
            throw new TooManyRequestsException();
        }

        if ((int)response.StatusCode < 500)
        {
            return response;
        }

        var id = response.Headers.TryGetValues(ApiRoutes.CorrelationIdHeaderName, out var values) ? values.FirstOrDefault() : null;
        var status = response.StatusCode;
        response.Dispose();
        throw new ServerErrorException(status, id);
    }
}

/// <summary>Something went wrong on the server. Still an <see cref="HttpRequestException"/>, so pages that handle failed requests still do.</summary>
public sealed class ServerErrorException(HttpStatusCode status, string? correlationId)
    : HttpRequestException($"The server couldn't complete the request ({(int)status}).", null, status)
{
    /// <summary>The id to quote when reporting the problem, if the server sent one.</summary>
    public string? CorrelationId { get; } = correlationId;
}

/// <summary>The server is limiting how often requests can be made (BUD-144). Its message can be shown as it is.</summary>
public sealed class TooManyRequestsException()
    : HttpRequestException(WaitMessage, null, HttpStatusCode.TooManyRequests)
{
    public const string WaitMessage = "There have been a lot of requests in a short time. Wait a moment, then try again.";
}
