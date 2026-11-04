using Budget.Domain.Common;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Budget.Infrastructure.Tests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class AuditingInterceptorTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedTime = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ModifiedTime = new(2026, 11, 5, 17, 15, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new() { Now = CreatedTime };
    private readonly TestCurrentUser _currentUser = new();

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task New_entity_records_who_created_it_and_when()
    {
        var creator = Guid.NewGuid();
        _currentUser.UserId = creator;
        var widget = new Widget { Name = "New" };

        await using (var db = CreateContext())
        {
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        }

        var saved = await LoadAsync(widget.Id);
        Assert.Equal(CreatedTime, saved.CreatedAt);
        Assert.Equal(creator, saved.CreatedBy);
        Assert.Equal(CreatedTime, saved.ModifiedAt);
        Assert.Equal(creator, saved.ModifiedBy);
    }

    [Fact]
    public async Task Updated_entity_records_who_modified_it_and_keeps_creation_details()
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var widget = new Widget { Name = "Original" };

        _currentUser.UserId = creator;
        await using (var db = CreateContext())
        {
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        }

        _clock.Now = ModifiedTime;
        _currentUser.UserId = editor;
        await using (var db = CreateContext())
        {
            var existing = await db.Widgets.SingleAsync(w => w.Id == widget.Id);
            existing.Name = "Changed";
            await db.SaveChangesAsync();
        }

        var saved = await LoadAsync(widget.Id);
        Assert.Equal("Changed", saved.Name);
        Assert.Equal(CreatedTime, saved.CreatedAt);
        Assert.Equal(creator, saved.CreatedBy);
        Assert.Equal(ModifiedTime, saved.ModifiedAt);
        Assert.Equal(editor, saved.ModifiedBy);
    }

    [Fact]
    public void Synchronous_save_also_records_audit_details()
    {
        _currentUser.UserId = null;
        var widget = new Widget { Name = "Sync" };

        using var db = CreateContext();
        db.Widgets.Add(widget);
        db.SaveChanges();

        Assert.Equal(CreatedTime, widget.CreatedAt);
        Assert.Null(widget.CreatedBy);
    }

    private async Task<Widget> LoadAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.Widgets.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    private AuditTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditTestDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString("AuditingTests"))
            .AddInterceptors(new AuditingInterceptor(_clock, _currentUser))
            .Options;

        return new AuditTestDbContext(options);
    }

    public sealed class Widget : AuditableEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class AuditTestDbContext(DbContextOptions<AuditTestDbContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
    }
}
