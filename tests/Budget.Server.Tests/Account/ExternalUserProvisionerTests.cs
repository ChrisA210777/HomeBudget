using System.Security.Claims;
using Budget.Domain.Households;
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
    public async Task First_sign_in_creates_My_Household_with_the_user_as_owner_and_admin()
    {
        var login = MicrosoftLogin(Guid.NewGuid().ToString("N"), "Jo Bloggs", "jo@outlook.com");

        ApplicationUser? user = null;
        await ExecuteScopeAsync(async services =>
            user = await services.GetRequiredService<ExternalUserProvisioner>().FindOrCreateAsync(login));

        Assert.NotNull(user);
        var userId = user.Id;
        await ExecuteDbContextAsync(async db =>
        {
            var membership = await db.HouseholdMembers.SingleAsync(m => m.UserId == userId);
            Assert.Equal(HouseholdRole.Admin, membership.Role);
            Assert.True(membership.IsOwner);

            var household = await db.Households.SingleAsync(h => h.Id == membership.HouseholdId);
            Assert.Equal("My Household", household.Name);

            var saved = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.Equal(household.Id, saved.LastActiveHouseholdId);

            var person = await db.People.IgnoreQueryFilters().SingleAsync(p => p.HouseholdId == household.Id);
            Assert.Equal("Jo Bloggs", person.DisplayName);
            Assert.Equal(userId, person.LinkedUserId);
        });
    }

    [Fact]
    public async Task If_any_part_of_sign_up_fails_nothing_is_created()
    {
        // Longer than the 450-character login key column, so linking the login fails after
        // the user and household have been saved, inside the same transaction.
        var email = $"{Guid.NewGuid():N}@example.com";
        var login = MicrosoftLogin(new string('k', 500), "Failing User", email);
        var householdsBefore = 0;
        var peopleBefore = 0;
        await ExecuteDbContextAsync(async db =>
        {
            householdsBefore = await db.Households.CountAsync();
            peopleBefore = await db.People.IgnoreQueryFilters().CountAsync();
        });

        ApplicationUser? user = null;
        await ExecuteScopeAsync(async services =>
            user = await services.GetRequiredService<ExternalUserProvisioner>().FindOrCreateAsync(login));

        Assert.Null(user);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
            Assert.Equal(householdsBefore, await db.Households.CountAsync());
            Assert.Equal(peopleBefore, await db.People.IgnoreQueryFilters().CountAsync());
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
        var userId = first.Id;
        await ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "alex@example.com"));
            Assert.Equal(1, await db.HouseholdMembers.CountAsync(m => m.UserId == userId));
        });
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
