using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Budget.Infrastructure.Tests.Persistence;

[Collection(SqlServerCollection.Name)]
public class MigrationTests(SqlServerFixture sqlServer)
{
    [Fact]
    public void Model_has_no_changes_missing_from_the_migrations()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The EF Core model has changes that aren't in a migration. Add a migration (see README).");
    }

    [Fact]
    public async Task Migrations_apply_cleanly_to_an_empty_database()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString("MigrationTests"))
            .Options;
        await using var db = new BudgetDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.False(await db.Users.AnyAsync());
    }
}
