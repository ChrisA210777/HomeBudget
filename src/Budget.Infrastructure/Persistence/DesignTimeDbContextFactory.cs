using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Budget.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tools (adding migrations, building the migration bundle).
/// The connection string here is never used to connect: the bundle is given the real one when
/// it runs, via <c>--connection</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BudgetDbContext>
{
    private const string PlaceholderConnectionString =
        "Server=localhost,1433;Database=HomeBudget;Integrated Security=False;TrustServerCertificate=True";

    public BudgetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlServer(PlaceholderConnectionString)
            .Options;

        return new BudgetDbContext(options);
    }
}
