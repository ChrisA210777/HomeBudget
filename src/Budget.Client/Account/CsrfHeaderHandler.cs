using Budget.Shared;

namespace Budget.Client.Account;

/// <summary>Adds the anti-forgery header the server requires on requests that change data.</summary>
public sealed class CsrfHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.TryAddWithoutValidation(ApiRoutes.CsrfHeaderName, "1");
        return base.SendAsync(request, cancellationToken);
    }
}
