using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Account;
using Budget.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Signs up a new user through the real first-sign-in path, which creates their own
    /// "My Household" with them as owner.
    /// </summary>
    protected async Task<(Guid UserId, Guid HouseholdId)> CreateHouseholdOwnerAsync(string displayName = "Household Owner")
    {
        var key = Guid.NewGuid().ToString("N");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, key), new Claim(ClaimTypes.Name, displayName)], "Microsoft"));

        ApplicationUser? user = null;
        await ExecuteScopeAsync(async services => user = await services.GetRequiredService<ExternalUserProvisioner>()
            .FindOrCreateAsync(new ExternalLoginInfo(principal, "Microsoft", key, "Microsoft")));

        Assert.NotNull(user);
        Assert.NotNull(user.LastActiveHouseholdId);
        return (user.Id, user.LastActiveHouseholdId.Value);
    }

    /// <summary>Creates a new user and adds them to <paramref name="householdId"/> with <paramref name="role"/>.</summary>
    protected async Task<Guid> AddMemberAsync(Guid householdId, HouseholdRole role, string displayName = "Household Member")
    {
        var user = await CreateUserAsync(displayName);
        await ExecuteDbContextAsync(async db =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == householdId);
            household.AddMember(user.Id, role, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });

        return user.Id;
    }

    /// <summary>
    /// Asserts that someone who isn't a member of a household gets 404 from a household-scoped
    /// endpoint, whether the household exists or not. Use this for every household-scoped endpoint.
    /// </summary>
    /// <param name="method">The endpoint's HTTP method.</param>
    /// <param name="urlForHousehold">Builds the endpoint's URL for a given household id.</param>
    /// <param name="body">Optional request body, for endpoints that need one to get past validation.</param>
    protected async Task AssertOtherHouseholdsCannotAccessAsync(HttpMethod method, Func<Guid, string> urlForHousehold, object? body = null)
    {
        ArgumentNullException.ThrowIfNull(urlForHousehold);

        var (_, otherHouseholdId) = await CreateHouseholdOwnerAsync("Someone else");
        var (outsiderId, _) = await CreateHouseholdOwnerAsync("Outsider");
        using var client = CreateClient(signedInAs: outsiderId, followRedirects: false);

        foreach (var householdId in new[] { otherHouseholdId, Guid.NewGuid() })
        {
            using var request = new HttpRequestMessage(method, new Uri(urlForHousehold(householdId), UriKind.Relative));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound,
                $"{method} {request.RequestUri} by a non-member returned {(int)response.StatusCode}, expected 404.");
        }
    }

    /// <summary>
    /// Asserts which roles can use a household-scoped endpoint: members below
    /// <paramref name="minimumRole"/> get 403, members at or above it don't.
    /// </summary>
    protected async Task AssertMinimumRoleAsync(HttpMethod method, Func<Guid, string> urlForHousehold, HouseholdRole minimumRole, Func<object?>? body = null)
    {
        ArgumentNullException.ThrowIfNull(urlForHousehold);

        var (_, householdId) = await CreateHouseholdOwnerAsync();

        foreach (var role in Enum.GetValues<HouseholdRole>())
        {
            var memberId = await AddMemberAsync(householdId, role, $"{role} member");
            using var client = CreateClient(signedInAs: memberId, followRedirects: false);
            using var request = new HttpRequestMessage(method, new Uri(urlForHousehold(householdId), UriKind.Relative));
            if (body?.Invoke() is { } content)
            {
                request.Content = JsonContent.Create(content);
            }

            using var response = await client.SendAsync(request);

            if (role.Satisfies(minimumRole))
            {
                Assert.True(
                    response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized),
                    $"{role} should be allowed to {method} {request.RequestUri} but got {(int)response.StatusCode}.");
            }
            else
            {
                Assert.True(
                    response.StatusCode == HttpStatusCode.Forbidden,
                    $"{role} should get 403 from {method} {request.RequestUri} but got {(int)response.StatusCode}.");
            }
        }
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
