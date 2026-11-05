using System.Security.Claims;
using Budget.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Budget.Server.Features.Account;

/// <summary>
/// Signs in as a named test person without an external provider, for the end-to-end tests
/// (BUD-40). Only mapped in the Testing environment when <c>TestSignIn:Enabled</c> is true, so it
/// can never exist in Development or Production.
/// </summary>
public static class TestSignInEndpoint
{
    public const string Route = "/account/test-sign-in";
    public const string EnvironmentName = "Testing";
    public const string ProviderName = "E2ETest";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsEnvironment(EnvironmentName) && configuration.GetValue<bool>("TestSignIn:Enabled");

    public static IEndpointRouteBuilder MapTestSignIn(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, SignInAsync).AllowAnonymous();
        return app;
    }

    /// <summary>Finds or creates the person (with their own household, as on a real first sign-in) and signs them in.</summary>
    private static async Task<IResult> SignInAsync(
        string name,
        string? returnUrl,
        SignInManager<ApplicationUser> signInManager,
        ExternalUserProvisioner provisioner)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, name.Trim()),
                new Claim(ClaimTypes.Email, $"{Guid.NewGuid():N}@e2e.test"),
            ],
            ProviderName);
        var login = new ExternalLoginInfo(new ClaimsPrincipal(identity), ProviderName, name.Trim().ToUpperInvariant(), "End-to-end tests");

        var user = await provisioner.FindOrCreateAsync(login);
        if (user is null)
        {
            return Results.Problem("Could not create the test user.");
        }

        await signInManager.SignInAsync(user, isPersistent: true, authenticationMethod: ProviderName);
        return Results.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
    }
}
