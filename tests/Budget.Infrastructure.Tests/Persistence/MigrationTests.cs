using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task AddPeople_gives_existing_household_owners_a_linked_person()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString("PeopleBackfillTests"))
            .Options;
        await using var db = new BudgetDbContext(options);
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();

        // A household created before people existed: an owner, plus a member who isn't the owner.
        await migrator.MigrateAsync("AddIncomeStreams");
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO AspNetUsers (Id, DisplayName, CreatedAt, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES ({ownerId}, N'Chris Austin', SYSDATETIMEOFFSET(), 0, 0, 0, 0, 0),
                   ({memberId}, N'Jane', SYSDATETIMEOFFSET(), 0, 0, 0, 0, 0);
            INSERT INTO Households (Id, Name, CurrencyCode, CreatedAt, ModifiedAt)
            VALUES ({householdId}, N'My Household', 'GBP', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            INSERT INTO HouseholdMembers (HouseholdId, UserId, Role, IsOwner, JoinedAt, CreatedAt, ModifiedAt)
            VALUES ({householdId}, {ownerId}, 'Admin', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                   ({householdId}, {memberId}, 'Viewer', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        await migrator.MigrateAsync();

        var person = await db.People.IgnoreQueryFilters().SingleAsync(p => p.HouseholdId == householdId);
        Assert.Equal("Chris Austin", person.DisplayName);
        Assert.Equal(ownerId, person.LinkedUserId);
    }
}
