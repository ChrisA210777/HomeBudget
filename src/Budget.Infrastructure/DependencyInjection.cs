using Budget.Infrastructure.Encryption;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Budget.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The name of the connection string in configuration (<c>ConnectionStrings:Budget</c>).</summary>
    public const string ConnectionStringName = "Budget";

    /// <summary>
    /// Registers infrastructure services: the EF Core DbContext with auditing, and the ASP.NET Core
    /// Identity user store. Register the host's own <see cref="ICurrentUser"/> before calling this;
    /// otherwise changes are recorded as made by the system.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<ActivityInterceptor>();
        services.AddSingleton<NameKeyInterceptor>();
        services.AddSingleton<EncryptedQueryGuard>();
        services.AddScoped<CurrentHousehold>();
        services.AddScoped<ICurrentHousehold>(sp => sp.GetRequiredService<CurrentHousehold>());

        // The connection string is read when the DbContext is first created rather than now, so
        // configuration added later (e.g. by integration tests) is picked up.
        services.AddDbContext<BudgetDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings:{ConnectionStringName}.");
            }

            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditingInterceptor>(),
                serviceProvider.GetRequiredService<ActivityInterceptor>(),
                serviceProvider.GetRequiredService<NameKeyInterceptor>(),
                serviceProvider.GetRequiredService<EncryptedQueryGuard>());
        });

        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<BudgetDbContext>()
            .AddSignInManager();

        return services;
    }
}
