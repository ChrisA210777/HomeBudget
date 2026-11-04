using Budget.Infrastructure.Identity;
using Budget.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Server.Tests.Identity;

/// <summary>Checks that the Identity user store is wired to the migrated database.</summary>
[Collection(ApiCollection.Name)]
public class UserStoreTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task User_with_external_login_is_saved_to_the_database()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var providerKey = Guid.NewGuid().ToString("N");

        await ExecuteScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test User" };

            AssertSucceeded(await users.CreateAsync(user));
            AssertSucceeded(await users.AddLoginAsync(user, new UserLoginInfo("Microsoft", providerKey, "Microsoft")));
        });

        await ExecuteDbContextAsync(async db =>
        {
            var saved = await db.Users.SingleAsync(u => u.Email == email);
            Assert.NotEqual(Guid.Empty, saved.Id);
            Assert.Equal("Test User", saved.DisplayName);
            Assert.Null(saved.PasswordHash);

            var login = await db.UserLogins.SingleAsync(l => l.UserId == saved.Id);
            Assert.Equal("Microsoft", login.LoginProvider);
            Assert.Equal(providerKey, login.ProviderKey);
        });
    }

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
