using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services: the EF Core DbContext, Identity stores and
    /// email sender. The DbContext and Identity are added in BUD-8.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services;
    }
}
