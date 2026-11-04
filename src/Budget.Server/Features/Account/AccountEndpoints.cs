using System.Security.Claims;
using Budget.Infrastructure.Identity;
using Budget.Shared;
using Budget.Shared.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Budget.Server.Features.Account;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AccountRoutes.Providers, GetProvidersAsync).AllowAnonymous();
        app.MapGet(AccountRoutes.LoginTemplate, LoginAsync).AllowAnonymous();
        app.MapGet(AccountRoutes.ExternalCallback, ExternalCallbackAsync).AllowAnonymous();
        app.MapGet(AccountRoutes.CurrentUser, GetCurrentUserAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(SignInManager<ApplicationUser> signInManager)
    {
        var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes.Select(s => new AuthProvider(s.Name, s.DisplayName ?? s.Name)).ToList();
    }

    /// <summary>Sends the browser to the provider's sign-in page.</summary>
    private static async Task<IResult> LoginAsync(string provider, string? returnUrl, SignInManager<ApplicationUser> signInManager)
    {
        var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
        var scheme = schemes.FirstOrDefault(s => string.Equals(s.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (scheme is null)
        {
            return Results.NotFound();
        }

        var callback = $"{AccountRoutes.ExternalCallback}?returnUrl={Uri.EscapeDataString(ReturnUrl.Sanitize(returnUrl))}";
        var properties = signInManager.ConfigureExternalAuthenticationProperties(scheme.Name, callback);
        return Results.Challenge(properties, [scheme.Name]);
    }

    /// <summary>
    /// The provider has signed the person in. Find or create their user, swap the temporary
    /// external cookie for the app's session cookie and send them back into the app.
    /// </summary>
    private static async Task<IResult> ExternalCallbackAsync(
        string? returnUrl,
        HttpContext httpContext,
        SignInManager<ApplicationUser> signInManager,
        ExternalUserProvisioner provisioner)
    {
        var login = await signInManager.GetExternalLoginInfoAsync();
        if (login is null)
        {
            return Results.Redirect(AccountRoutes.LoginPageWithError(LoginErrors.Failed));
        }

        var user = await provisioner.FindOrCreateAsync(login);
        await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (user is null)
        {
            return Results.Redirect(AccountRoutes.LoginPageWithError(LoginErrors.Failed));
        }

        await signInManager.SignInAsync(user, isPersistent: true, authenticationMethod: login.LoginProvider);
        return Results.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
    }

    private static async Task<IResult> GetCurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            // Signed in, but the user no longer exists.
            return Results.Unauthorized();
        }

        // Households are added in BUD-14.
        return Results.Ok(new CurrentUserResponse(user.Id, user.DisplayName, user.Email, [], LastActiveHouseholdId: null));
    }
}
