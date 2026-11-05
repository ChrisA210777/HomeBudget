using Budget.Domain.Households;
using Budget.Domain.People;
using Budget.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.DisplayName).HasMaxLength(Person.DisplayNameMaxLength).IsRequired();

        // Names are unique within a household. The database collation is case-insensitive,
        // so "jane" and "Jane" count as the same name.
        builder.HasIndex(p => new { p.HouseholdId, p.DisplayName }).IsUnique();

        // A login is linked to at most one person per household.
        builder.HasIndex(p => new { p.HouseholdId, p.LinkedUserId })
            .IsUnique()
            .HasFilter("[LinkedUserId] IS NOT NULL");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(p => p.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unlinked in code if the login goes; no database cascade from users.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.LinkedUserId)
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}
