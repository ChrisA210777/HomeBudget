using System.Net;
using System.Net.Http.Json;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Client.Account;

public interface IAccountApi
{
    Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default);

    /// <returns>The signed-in user, or <c>null</c> if nobody is signed in.</returns>
    Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

public sealed class AccountApi(HttpClient http) : IAccountApi
{
    public async Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<AuthProvider>>(new Uri(AccountRoutes.Providers, UriKind.Relative), cancellationToken) ?? [];

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri(AccountRoutes.CurrentUser, UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
    }
}
