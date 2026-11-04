using System.Security.Claims;
using Budget.Client.Account;
using Budget.Shared.Account;

namespace Budget.Client.Tests.Account;

public class BffAuthenticationStateProviderTests
{
    [Fact]
    public async Task Signed_in_user_becomes_an_authenticated_principal()
    {
        var userId = Guid.NewGuid();
        var api = new StubAccountApi(new CurrentUserResponse(userId, "Chris Austin", "chris@outlook.com", [], null));

        var state = await new BffAuthenticationStateProvider(new UserSession(api)).GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal("Chris Austin", state.User.Identity?.Name);
        Assert.Equal(userId.ToString(), state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("chris@outlook.com", state.User.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task Nobody_signed_in_is_anonymous()
    {
        var state = await new BffAuthenticationStateProvider(new UserSession(new StubAccountApi(null))).GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Server_unreachable_is_treated_as_signed_out()
    {
        var api = new StubAccountApi(null) { Error = new HttpRequestException("offline") };

        var state = await new BffAuthenticationStateProvider(new UserSession(api)).GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    private sealed class StubAccountApi(CurrentUserResponse? user) : IAccountApi
    {
        public Exception? Error { get; init; }

        public Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthProvider>>([]);

        public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            Error is null ? Task.FromResult(user) : Task.FromException<CurrentUserResponse?>(Error);
    }
}
