using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Shared.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.SignIn;

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

    /// <summary>
    /// Finds or creates the person (with their own household, as on a real first sign-in) and signs them in.
    /// With <paramref name="joinHouseholdOf"/>, they're also added to the household owned by that test person, with
    /// <paramref name="joinAs"/> (default User), and it becomes their active household. Until invitations exist
    /// (BUD-23), this is how the tests get a household with several members. They've accepted the terms of use unless
    /// <paramref name="acceptTerms"/> is false.
    /// </summary>
    private static async Task<IResult> SignInAsync(
        string name,
        string? returnUrl,
        string? joinHouseholdOf,
        string? joinAs,
        bool? acceptTerms,
        SignInManager<ApplicationUser> signInManager,
        ExternalUserProvisioner provisioner,
        BudgetDbContext db,
        TimeProvider clock)
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

        if (!string.IsNullOrWhiteSpace(joinHouseholdOf))
        {
            var role = Enum.TryParse<HouseholdRole>(joinAs, out var parsed) && Enum.IsDefined(parsed) ? parsed : HouseholdRole.User;
            var owner = await signInManager.UserManager.FindByLoginAsync(ProviderName, joinHouseholdOf.Trim().ToUpperInvariant());
            var household = owner is null
                ? null
                : await db.Households.Include(h => h.Members).FirstOrDefaultAsync(h => h.Members.Any(m => m.UserId == owner.Id && m.IsOwner));
            if (household is null)
            {
                return Results.BadRequest();
            }

            if (household.Members.All(m => m.UserId != user.Id))
            {
                household.AddMember(user.Id, role, clock.GetUtcNow());
            }

            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id);
            tracked.LastActiveHouseholdId = household.Id;
            await db.SaveChangesAsync();
        }

        // Test people have accepted the terms (BUD-152), unless a test is checking what happens when they haven't.
        if (acceptTerms != false && user.TermsAcceptedVersion != LegalDocuments.TermsVersion)
        {
            var accepting = await db.Users.SingleAsync(u => u.Id == user.Id);
            accepting.TermsAcceptedVersion = LegalDocuments.TermsVersion;
            accepting.TermsAcceptedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
        }

        await signInManager.SignInAsync(user, isPersistent: true, authenticationMethod: ProviderName);
        return Results.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
    }
}
