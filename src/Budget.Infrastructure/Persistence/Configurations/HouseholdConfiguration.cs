using Budget.Domain.Households;
using Budget.Infrastructure.Encryption;
using Budget.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("Households");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Name).HasMaxLength(Household.NameMaxLength).IsRequired().IsEncrypted();
        builder.Property(h => h.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();

        builder.HasMany(h => h.Members)
            .WithOne()
            .HasForeignKey(m => m.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(h => h.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class HouseholdMemberConfiguration : IEntityTypeConfiguration<HouseholdMember>
{
    public void Configure(EntityTypeBuilder<HouseholdMember> builder)
    {
        builder.ToTable("HouseholdMembers");
        builder.HasKey(m => new { m.HouseholdId, m.UserId });
        builder.HasIndex(m => m.UserId);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.Property(u => u.TermsAcceptedVersion).HasMaxLength(ApplicationUser.TermsVersionMaxLength);

        // No cascade in the database: clearing this when a household is deleted is done in code,
        // which avoids SQL Server's "multiple cascade paths" restriction.
        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(u => u.LastActiveHouseholdId)
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}
