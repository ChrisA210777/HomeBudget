using Budget.Domain.Households;
using Budget.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Budget.Infrastructure.Persistence;

/// <summary>
/// The application's database. It includes the ASP.NET Core Identity user tables (users,
/// external logins, claims and tokens) but not Identity's role tables: roles in this app
/// are per household and live on the household membership (see Roles &amp; Permissions).
/// </summary>
public class BudgetDbContext(DbContextOptions<BudgetDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Household> Households => Set<Household>();

    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(BudgetDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money is stored as entered with four decimal places (see ADR-007).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
    }
}
