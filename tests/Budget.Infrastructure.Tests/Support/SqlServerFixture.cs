using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Budget.Infrastructure.Tests.Support;

/// <summary>
/// A real SQL Server running in Docker, shared by every test in the <see cref="SqlServerCollection"/>.
/// Each test class should use its own database name so tests don't interfere.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string GetConnectionString(string database) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
