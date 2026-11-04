using Budget.Domain.Households;
using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Budget.Infrastructure.Tests.Persistence;

/// <summary>
/// Checks the household query filter, the second line of defence for household isolation,
/// using a stand-in household-owned entity.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class HouseholdQueryFilterTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private static readonly Guid HouseholdA = Guid.NewGuid();
    private static readonly Guid HouseholdB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = CreateContext(currentHousehold: null);
        await db.Database.EnsureCreatedAsync();
        if (!await db.Notes.IgnoreQueryFilters().AnyAsync())
        {
            db.Notes.AddRange(
                new Note { Id = Guid.NewGuid(), HouseholdId = HouseholdA, Text = "A1" },
                new Note { Id = Guid.NewGuid(), HouseholdId = HouseholdA, Text = "A2" },
                new Note { Id = Guid.NewGuid(), HouseholdId = HouseholdB, Text = "B1" });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Queries_only_return_the_current_households_rows()
    {
        await using var db = CreateContext(HouseholdA);

        var notes = await db.Notes.Select(n => n.Text).OrderBy(t => t).ToListAsync();

        Assert.Equal(new[] { "A1", "A2" }, notes);
    }

    [Fact]
    public async Task Rows_of_another_household_cannot_be_found_even_by_id()
    {
        Guid otherId;
        await using (var unfiltered = CreateContext(currentHousehold: null))
        {
            otherId = await unfiltered.Notes.IgnoreQueryFilters().Where(n => n.HouseholdId == HouseholdB).Select(n => n.Id).SingleAsync();
        }

        await using var db = CreateContext(HouseholdA);

        Assert.Null(await db.Notes.SingleOrDefaultAsync(n => n.Id == otherId));
    }

    [Fact]
    public async Task Without_a_current_household_nothing_is_returned()
    {
        await using var db = CreateContext(currentHousehold: null);

        Assert.Empty(await db.Notes.ToListAsync());
    }

    [Fact]
    public async Task The_current_household_is_read_each_time_a_query_runs()
    {
        var current = new MutableCurrentHousehold { HouseholdId = HouseholdA };
        await using var db = CreateContextFor(current);

        var forA = await db.Notes.CountAsync();
        current.HouseholdId = HouseholdB;
        var forB = await db.Notes.CountAsync();

        Assert.Equal(2, forA);
        Assert.Equal(1, forB);
    }

    private FilterTestDbContext CreateContext(Guid? currentHousehold) =>
        CreateContextFor(new MutableCurrentHousehold { HouseholdId = currentHousehold });

    private FilterTestDbContext CreateContextFor(ICurrentHousehold currentHousehold)
    {
        var options = new DbContextOptionsBuilder<FilterTestDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString("HouseholdFilterTests"))
            .Options;
        return new FilterTestDbContext(options, currentHousehold);
    }

    public sealed class Note : IHouseholdOwned
    {
        public Guid Id { get; set; }

        public Guid HouseholdId { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    private sealed class MutableCurrentHousehold : ICurrentHousehold
    {
        public Guid? HouseholdId { get; set; }
    }

    private sealed class FilterTestDbContext(DbContextOptions<FilterTestDbContext> options, ICurrentHousehold currentHousehold)
        : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        private Guid? CurrentHouseholdId => currentHousehold.HouseholdId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Note>();
            modelBuilder.ApplyHouseholdQueryFilters(() => CurrentHouseholdId);
        }
    }
}
