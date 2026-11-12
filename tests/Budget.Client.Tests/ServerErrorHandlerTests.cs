using System.Net;
using Budget.Shared;

namespace Budget.Client.Tests;

public sealed class ServerErrorHandlerTests
{
    [Fact]
    public async Task A_server_error_becomes_an_exception_with_its_correlation_id()
    {
        using var client = Client(HttpStatusCode.InternalServerError, "4bf92f3577b34da6a3ce929d0e0e4736");

        var error = await Assert.ThrowsAsync<ServerErrorException>(() => client.GetAsync(new Uri("/api/thing", UriKind.Relative)));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", error.CorrelationId);
        Assert.IsAssignableFrom<HttpRequestException>(error);
    }

    [Fact]
    public async Task Too_many_requests_becomes_an_exception_that_says_to_wait()
    {
        using var client = Client(HttpStatusCode.TooManyRequests, "abc");

        var error = await Assert.ThrowsAsync<TooManyRequestsException>(() => client.GetAsync(new Uri("/api/thing", UriKind.Relative)));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(TooManyRequestsException.WaitMessage, error.Message);
        Assert.IsAssignableFrom<HttpRequestException>(error);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Other_responses_pass_through(HttpStatusCode status)
    {
        using var client = Client(status, "abc");

        using var response = await client.GetAsync(new Uri("/api/thing", UriKind.Relative));

        Assert.Equal(status, response.StatusCode);
    }

    private static HttpClient Client(HttpStatusCode status, string correlationId) =>
        new(new ServerErrorHandler { InnerHandler = new Stub(status, correlationId) }) { BaseAddress = new Uri("https://budget.test") };

    private sealed class Stub(HttpStatusCode status, string correlationId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Add(ApiRoutes.CorrelationIdHeaderName, correlationId);
            return Task.FromResult(response);
        }
    }
}
