using System.Security.Claims;
using Budget.Infrastructure.Identity;
using Budget.Server.Features.Account;
using Budget.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Server.Tests.Account;

[Collection(ApiCollection.Name)]
public class ExternalUserProvisionerTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task First_sign_in_creates_a_user_with_the_provider_details()
    {
        var providerKey = Guid.NewGuid().ToString("N");
        var login = MicrosoftLogin(providerKey, "Sam Smith", "sam@outlook.com");

        ApplicationUser? user = null;
        await ExecuteScopeAsync(async services =>
            user = await services.GetRequiredService<ExternalUserProvisioner>().FindOrCreateAsync(login));

        Assert.NotNull(user);
        var userId = user.Id;
        await ExecuteDbContextAsync(async db =>
        {
            var saved = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.Equal("Sam Smith", saved.DisplayName);
            Assert.Equal("sam@outlook.com", saved.Email);
            Assert.Null(saved.PasswordHash);
            Assert.NotEqual(default, saved.CreatedAt);

            var savedLogin = await db.UserLogins.SingleAsync(l => l.UserId == userId);
            Assert.Equal("Microsoft", savedLogin.LoginProvider);
            Assert.Equal(providerKey, savedLogin.ProviderKey);
        });
    }

    [Fact]
    public async Task Signing_in_again_returns_the_same_user()
    {
        var login = MicrosoftLogin(Guid.NewGuid().ToString("N"), "Alex Jones", "alex@example.com");

        ApplicationUser? first = null;
        ApplicationUser? second = null;
        await ExecuteScopeAsync(async services =>
            first = await services.GetRequiredService<ExternalUserProvisioner>().FindOrCreateAsync(login));
        await ExecuteScopeAsync(async services =>
            second = await services.GetRequiredService<ExternalUserProvisioner>().FindOrCreateAsync(login));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        await ExecuteDbContextAsync(async db =>
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "alex@example.com")));
    }

    [Fact]
    public async Task Display_name_falls_back_to_email_when_the_provider_has_no_name()
    {
        var login = MicrosoftLogin(Guid.NewGuid().ToString("N"), name: null, email: "noname@example.com");

        ApplicationUser? user = null;
        await ExecuteScopeAsync(async services =>
            user = await services.GetRequiredService<ExternalUserProvisioner>().FindOrCreateAsync(login));

        Assert.NotNull(user);
        Assert.Equal("noname@example.com", user.DisplayName);
    }

    private static ExternalLoginInfo MicrosoftLogin(string providerKey, string? name, string? email)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, providerKey) };
        if (name is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        if (email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Microsoft"));
        return new ExternalLoginInfo(principal, "Microsoft", providerKey, "Microsoft");
    }
}
