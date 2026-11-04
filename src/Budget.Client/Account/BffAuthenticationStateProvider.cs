using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Budget.Client.Account;

/// <summary>
/// Asks the server who is signed in. The session itself is an HTTP-only cookie the client
/// can't read (Backend-for-Frontend, see ADR-001).
/// </summary>
public sealed class BffAuthenticationStateProvider(IAccountApi accountApi) : AuthenticationStateProvider
{
    public const string AuthenticationType = "bff";

    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var user = await accountApi.GetCurrentUserAsync();
            if (user is null)
            {
                return Anonymous;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.DisplayName),
            };
            if (!string.IsNullOrEmpty(user.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, user.Email));
            }

            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)));
        }
        catch (HttpRequestException)
        {
            // The server couldn't be reached; treat as signed out rather than crashing the app.
            return Anonymous;
        }
    }
}
