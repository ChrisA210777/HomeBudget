using Budget.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Budget.Server.Features.SignIn;

public static class AuthenticationSetup
{
    public const string MicrosoftConfigSection = "Authentication:Microsoft";
    public const string GoogleConfigSection = "Authentication:Google";

    /// <summary>A claim saying the provider has verified the email address (BUD-102).</summary>
    public const string EmailVerifiedClaim = "email_verified";

    /// <summary>
    /// Cookie-based sign-in with external providers (Backend-for-Frontend, see ADR-001 and ADR-002).
    /// The browser only ever holds an HTTP-only session cookie; no tokens reach the client.
    /// A provider is only enabled when its credentials are configured.
    /// </summary>
    public static IServiceCollection AddBudgetAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-budget";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            // Lax, not Strict: the cookie must be sent on the redirect back from the provider.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.LoginPath = AccountRoutes.LoginPage;
            options.Events = new CookieAuthenticationEvents
            {
                // API calls get a status code; the client decides what to show.
                OnRedirectToLogin = context => RespondWithStatusForApi(context, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => RespondWithStatusForApi(context, StatusCodes.Status403Forbidden),
            };
        });

        var microsoft = configuration.GetSection(MicrosoftConfigSection);
        if (!string.IsNullOrWhiteSpace(microsoft["ClientId"]) && !string.IsNullOrWhiteSpace(microsoft["ClientSecret"]))
        {
            // Uses the "common" endpoint, so both personal Microsoft accounts (outlook.com,
            // hotmail.com, live.com) and work or school accounts can sign in.
            authentication.AddMicrosoftAccount(options =>
            {
                options.ClientId = microsoft["ClientId"]!;
                options.ClientSecret = microsoft["ClientSecret"]!;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Events.OnAccessDenied = context =>
                {
                    context.Response.Redirect(AccountRoutes.LoginPageWithError(LoginErrors.Cancelled));
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect(AccountRoutes.LoginPageWithError(LoginErrors.Failed));
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        // Google (ADR-015, BUD-102). Google's emails are verified, which lets a Google login join the user who
        // already has that email.
        var google = configuration.GetSection(GoogleConfigSection);
        if (!string.IsNullOrWhiteSpace(google["ClientId"]) && !string.IsNullOrWhiteSpace(google["ClientSecret"]))
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"]!;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");
                options.Events.OnAccessDenied = context =>
                {
                    context.Response.Redirect(AccountRoutes.LoginPageWithError(LoginErrors.Cancelled));
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect(AccountRoutes.LoginPageWithError(LoginErrors.Failed));
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddScoped<ExternalUserProvisioner>();

        return services;
    }

    private static Task RespondWithStatusForApi(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = statusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }

    internal static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(ApiRoutes.ApiBase, StringComparison.OrdinalIgnoreCase)
        || request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase);
}
