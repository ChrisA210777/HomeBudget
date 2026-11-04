using Budget.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace Budget.Server.Tests.Infrastructure;

/// <summary>
/// Runs the real application against a real SQL Server in a Docker container. The app starts in
/// Development, so it applies the EF Core migrations to the container's database on startup.
/// One instance is shared by every test in the <see cref="ApiCollection"/>.
/// </summary>
public sealed class BudgetApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();

    public async Task InitializeAsync() => await _sqlServer.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _sqlServer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = "HomeBudgetApiTests",
        }.ConnectionString;

        builder.UseEnvironment("Development");

        // Added last, so these override appsettings.Development.json.
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString,
                ["Database:MigrateOnStartup"] = "true",
            }));
    }
}
