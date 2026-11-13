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

    [Fact]
    public async Task AddAccounts_turns_paid_from_names_into_accounts_and_pots()
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString("AccountsConversionTests"))
            .Options;
        await using var db = new BudgetDbContext(options);
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();

        // Before accounts (BUD-89): "paid from" was free text, with pots written as "Account (Pot Pot)".
        await migrator.MigrateAsync("AddUserSettings");
        var householdId = Guid.NewGuid();
        var otherHouseholdId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var otherCategoryId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO Households (Id, Name, CurrencyCode, CreatedAt, ModifiedAt)
            VALUES ({householdId}, N'Ours', 'GBP', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                   ({otherHouseholdId}, N'Theirs', 'GBP', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            INSERT INTO OutgoingCategories (Id, HouseholdId, Name, SortOrder, IsHidden, Icon, CreatedAt, ModifiedAt)
            VALUES ({categoryId}, {householdId}, N'Housing', 1, 0, N'home', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                   ({otherCategoryId}, {otherHouseholdId}, N'Housing', 1, 0, N'home', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        var outgoings = new (string Name, Guid Household, Guid Category, string? PaidFrom)[]
        {
            ("Mortgage", householdId, categoryId, "Joint bills"),
            ("Council tax", householdId, categoryId, " joint BILLS "),
            ("Holiday", householdId, categoryId, "Joint Monzo Account (Holiday Pot)"),
            ("Garden", householdId, categoryId, "Joint Monzo Account (garden pot)"),
            ("Food", householdId, categoryId, "Joint Monzo Account"),
            ("Gym", householdId, categoryId, null),
            ("Rent", otherHouseholdId, otherCategoryId, "Joint bills"),
        };
        foreach (var (name, household, category, paidFrom) in outgoings)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO Outgoings (Id, HouseholdId, Name, Kind, Priority, CategoryId, FirstPaymentDate, RenewalEveryMonths, AutoRenews, AlertPeriod, PaidFrom, CreatedAt, ModifiedAt)
                VALUES (NEWID(), {household}, {name}, N'Bill', N'Essential', {category}, '2026-01-01', 12, 0, N'None', {paidFrom}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                """);
        }

        await migrator.MigrateAsync();

        var accounts = await db.Accounts.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        // Names are encrypted once the app runs (BUD-166), so they're matched in memory.
        var savedOutgoings = await db.Outgoings.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        string? PaidFrom(string outgoing)
        {
            var accountId = savedOutgoings.Single(o => o.Name == outgoing).AccountId;
            var account = accounts.SingleOrDefault(a => a.Id == accountId);
            return account?.DisplayName(accounts.SingleOrDefault(a => a.Id == account.ParentAccountId));
        }

        // One account per name in each household, ignoring case and spaces; pots inside their account.
        Assert.Equal(
            new[] { "Joint bills", "Joint Monzo Account", "Joint Monzo Account › garden", "Joint Monzo Account › Holiday" },
            accounts.Where(a => a.HouseholdId == householdId).Select(a => a.DisplayName(accounts.SingleOrDefault(p => p.Id == a.ParentAccountId))).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(accounts, a => Assert.Null(a.OwnerPersonId));
        Assert.Equal("Joint bills", PaidFrom("Mortgage"));
        Assert.Equal("Joint bills", PaidFrom("Council tax"));
        Assert.Equal("Joint Monzo Account › Holiday", PaidFrom("Holiday"));
        Assert.Equal("Joint Monzo Account › garden", PaidFrom("Garden"));
        Assert.Equal("Joint Monzo Account", PaidFrom("Food"));
        Assert.Null(PaidFrom("Gym"));

        // Each household gets its own accounts.
        var rent = savedOutgoings.Single(o => o.Name == "Rent");
        Assert.Equal(otherHouseholdId, accounts.Single(a => a.Id == rent.AccountId).HouseholdId);
    }
}
