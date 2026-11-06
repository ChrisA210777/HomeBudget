using System.Security.Claims;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Shared;
using Budget.Shared.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Account;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AccountRoutes.Providers, GetProvidersAsync).AllowAnonymous();
        app.MapGet(AccountRoutes.LoginTemplate, LoginAsync).AllowAnonymous();
        app.MapGet(AccountRoutes.ExternalCallback, ExternalCallbackAsync).AllowAnonymous();
        app.MapGet(AccountRoutes.CurrentUser, GetCurrentUserAsync).RequireAuthorization();
        app.MapPut(AccountRoutes.ActiveHousehold, SetActiveHouseholdAsync).RequireAuthorization();

        // Anonymous so that signing out always succeeds, even if the session has already expired.
        app.MapPost(AccountRoutes.Logout, LogoutAsync).AllowAnonymous();

        return app;
    }

    /// <summary>Switches the user's active household (BUD-20). It's remembered for their next sign-in.</summary>
    private static async Task<IResult> SetActiveHouseholdAsync(
        SetActiveHouseholdRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        BudgetDbContext db)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Not a member, or no such household: the same answer, so it doesn't reveal which exist.
        if (!await db.HouseholdMembers.AnyAsync(m => m.UserId == user.Id && m.HouseholdId == request.HouseholdId))
        {
            return Results.NotFound();
        }

        user.LastActiveHouseholdId = request.HouseholdId;
        var updated = await users.UpdateAsync(user);
        return updated.Succeeded ? Results.NoContent() : Results.Problem("Couldn't switch household.");
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

    /// <summary>Clears the session cookie (and any half-finished external sign-in).</summary>
    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        BudgetDbContext db)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            // Signed in, but the user no longer exists.
            return Results.Unauthorized();
        }

        var households = await (
                from member in db.HouseholdMembers
                join household in db.Households on member.HouseholdId equals household.Id
                where member.UserId == user.Id
                orderby household.Name

                // Across all the user's households, so the per-household filter doesn't apply here.
                let person = db.People.IgnoreQueryFilters()
                    .Where(p => p.HouseholdId == household.Id && p.LinkedUserId == user.Id)
                    .Select(p => new { p.Id, p.DisplayName })
                    .FirstOrDefault()
                select new HouseholdMembershipSummary(
                    household.Id, household.Name, member.Role.ToString(), member.IsOwner, person == null ? null : person.Id, person == null ? null : person.DisplayName))
            .ToListAsync();

        // Fall back to another household if the last active one is gone or no longer theirs.
        var activeHouseholdId = households.Any(h => h.HouseholdId == user.LastActiveHouseholdId)
            ? user.LastActiveHouseholdId
            : households.FirstOrDefault()?.HouseholdId;

        return Results.Ok(new CurrentUserResponse(user.Id, user.DisplayName, user.Email, households, activeHouseholdId));
    }
}
