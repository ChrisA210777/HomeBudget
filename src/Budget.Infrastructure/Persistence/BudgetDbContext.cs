using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Budget.Infrastructure.Persistence;

/// <summary>
/// The application's database. It includes the ASP.NET Core Identity user tables (users,
/// external logins, claims and tokens) but not Identity's role tables: roles in this app
/// are per household and live on the household membership (see Roles &amp; Permissions).
/// </summary>
public class BudgetDbContext : IdentityUserContext<ApplicationUser, Guid>
{
    private readonly ICurrentHousehold _currentHousehold;

    /// <summary>For tools and tests: no current household, so household-owned data is hidden.</summary>
    public BudgetDbContext(DbContextOptions<BudgetDbContext> options)
        : this(options, NoCurrentHousehold.Instance)
    {
    }

    public BudgetDbContext(DbContextOptions<BudgetDbContext> options, ICurrentHousehold currentHousehold)
        : base(options)
    {
        _currentHousehold = currentHousehold ?? throw new ArgumentNullException(nameof(currentHousehold));
    }

    public DbSet<Household> Households => Set<Household>();

    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();

    /// <summary>Filtered to the current household (see <see cref="ICurrentHousehold"/>).</summary>
    public DbSet<IncomeStream> IncomeStreams => Set<IncomeStream>();

    /// <summary>Read by the household query filters each time a query runs.</summary>
    private Guid? CurrentHouseholdId => _currentHousehold.HouseholdId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(BudgetDbContext).Assembly);
        builder.ApplyHouseholdQueryFilters(() => CurrentHouseholdId);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money is stored as entered with four decimal places (see ADR-007).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
    }

    private sealed class NoCurrentHousehold : ICurrentHousehold
    {
        public static readonly NoCurrentHousehold Instance = new();

        public Guid? HouseholdId => null;
    }
}
