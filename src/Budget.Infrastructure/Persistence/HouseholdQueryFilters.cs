using System.Linq.Expressions;
using Budget.Domain.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Infrastructure.Persistence;

public static class HouseholdQueryFilters
{
    /// <summary>
    /// Adds a query filter to every <see cref="IHouseholdOwned"/> entity so that queries only return
    /// rows for the current household. When there is no current household nothing is returned:
    /// the safe default. This backs up the authorisation check (see ADR-003).
    /// </summary>
    /// <param name="currentHouseholdId">
    /// An expression reading the current household from the DbContext, e.g. <c>() => CurrentHouseholdId</c>,
    /// so EF Core evaluates it for each query.
    /// </param>
    public static ModelBuilder ApplyHouseholdQueryFilters(this ModelBuilder modelBuilder, Expression<Func<Guid?>> currentHouseholdId)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(currentHouseholdId);

        var ownedRootTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(IHouseholdOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in ownedRootTypes)
        {
            var entity = Expression.Parameter(clrType, "e");
            var householdId = Expression.Convert(Expression.Property(entity, nameof(IHouseholdOwned.HouseholdId)), typeof(Guid?));
            var filter = Expression.Lambda(Expression.Equal(householdId, currentHouseholdId.Body), entity);

            modelBuilder.Entity(clrType).HasQueryFilter(filter);
        }

        return modelBuilder;
    }
}
