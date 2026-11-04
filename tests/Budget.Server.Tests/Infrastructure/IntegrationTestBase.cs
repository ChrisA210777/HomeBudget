using Budget.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Server.Tests.Infrastructure;

/// <summary>
/// Base class for integration tests that call the API and work with the real database.
/// Derived classes must also be marked <c>[Collection(ApiCollection.Name)]</c>.
/// </summary>
public abstract class IntegrationTestBase(BudgetApiFactory factory)
{
    protected BudgetApiFactory Factory { get; } = factory;

    protected HttpClient CreateClient() => Factory.CreateClient();

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
