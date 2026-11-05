using Budget.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public const string MicrosoftClientId = "test-client-id";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();

    public BudgetApiFactory()
    {
        // Sign-in providers are registered while Program builds its services, before the
        // configuration below is applied, so their settings are supplied as environment variables.
        Environment.SetEnvironmentVariable("Authentication__Microsoft__ClientId", MicrosoftClientId);
        Environment.SetEnvironmentVariable("Authentication__Microsoft__ClientSecret", "test-client-secret");
    }

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

                // Only honoured in the Testing environment; set here to prove it stays off in Development.
                ["TestSignIn:Enabled"] = "true",
            }));

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Who you are comes from the test header; signing in and challenges still use the
            // app's real cookie and Microsoft schemes.
            services.Configure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName);
        });
    }
}
