using System.Security.Claims;
using Budget.Infrastructure.Identity;
using Budget.Server.Features.SignIn;
using Budget.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Server.Tests.SignIn;

/// <summary>One user per verified email across sign-in providers (ADR-015, BUD-102).</summary>
[Collection(ApiCollection.Name)]
public class SameEmailSignInTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_google_login_with_a_verified_email_joins_the_user_who_has_it()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var existing = await CreateUserAsync("Jane Austin", email);

        var user = await SignInAsync("Google", email, verified: true);

        Assert.Equal(existing.Id, user!.Id);
        await ExecuteScopeAsync(async services =>
        {
            var logins = await services.GetRequiredService<UserManager<ApplicationUser>>().GetLoginsAsync(user);
            Assert.Contains(logins, l => l.LoginProvider == "Google");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task An_unverified_email_gets_a_user_of_its_own(bool? verified)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var existing = await CreateUserAsync("Jane Austin", email);

        var user = await SignInAsync("Microsoft", email, verified);

        Assert.NotEqual(existing.Id, user!.Id);
    }

    private async Task<ApplicationUser?> SignInAsync(string provider, string email, bool? verified)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, "Jane Austin"),
            new(ClaimTypes.Email, email),
        };
        if (verified is { } value)
        {
            claims.Add(new Claim(AuthenticationSetup.EmailVerifiedClaim, value ? "True" : "False"));
        }

        var key = claims[0].Value;
        ApplicationUser? user = null;
        await ExecuteScopeAsync(async services => user = await services.GetRequiredService<ExternalUserProvisioner>()
            .FindOrCreateAsync(new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity(claims, provider)), provider, key, provider)));
        return user;
    }
}
