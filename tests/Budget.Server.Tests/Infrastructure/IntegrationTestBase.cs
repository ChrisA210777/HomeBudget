using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Server.Tests.Infrastructure;

/// <summary>
/// Base class for integration tests that call the API and work with the real database.
/// Derived classes must also be marked <c>[Collection(ApiCollection.Name)]</c>.
/// </summary>
public abstract class IntegrationTestBase(BudgetApiFactory factory)
{
    protected BudgetApiFactory Factory { get; } = factory;

    /// <summary>
    /// Creates a client that sends the anti-forgery header, like the real client does.
    /// Pass <paramref name="signedInAs"/> to make requests as that user.
    /// </summary>
    protected HttpClient CreateClient(Guid? signedInAs = null, bool followRedirects = true)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });
        client.DefaultRequestHeaders.Add(ApiRoutes.CsrfHeaderName, "1");
        if (signedInAs is { } userId)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        }

        return client;
    }

    /// <summary>Creates a user directly in the database, as if they had signed up.</summary>
    protected async Task<ApplicationUser> CreateUserAsync(string displayName = "Test User", string? email = null)
    {
        var user = new ApplicationUser
        {
            UserName = Guid.NewGuid().ToString("N"),
            Email = email ?? $"{Guid.NewGuid():N}@example.com",
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await ExecuteScopeAsync(async services =>
        {
            var result = await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        });

        return user;
    }

    /// <summary>Runs <paramref name="action"/> with services from a new DI scope, as a request would.</summary>
    protected async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    /// <summary>Runs <paramref name="action"/> with a fresh <see cref="BudgetDbContext"/>.</summary>
    protected Task ExecuteDbContextAsync(Func<BudgetDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return ExecuteScopeAsync(services => action(services.GetRequiredService<BudgetDbContext>()));
    }
}
