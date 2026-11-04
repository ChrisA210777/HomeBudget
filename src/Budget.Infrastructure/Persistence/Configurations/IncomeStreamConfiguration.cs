using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

/// <summary>
/// All income stream types share one table (table-per-hierarchy), told apart by the Type column.
/// See ADR-003.
/// </summary>
public sealed class IncomeStreamConfiguration : IEntityTypeConfiguration<IncomeStream>
{
    /// <summary>Shadow property used for optimistic concurrency (SQL Server rowversion).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<IncomeStream> builder)
    {
        builder.ToTable("IncomeStreams");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(IncomeStream.NameMaxLength).IsRequired();
        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property<byte[]>(RowVersion).IsRowVersion();

        builder.HasDiscriminator(s => s.Type)
            .HasValue<EmploymentIncome>(IncomeStreamType.Employment);

        builder.HasIndex(s => s.HouseholdId);

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(s => s.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cleared in code when a member leaves; no database cascade from users.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.MemberUserId)
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}

public sealed class EmploymentIncomeConfiguration : IEntityTypeConfiguration<EmploymentIncome>
{
    public void Configure(EntityTypeBuilder<EmploymentIncome> builder)
    {
        builder.Property(e => e.EmployerName).HasMaxLength(EmploymentIncome.EmployerNameMaxLength);
        builder.Property(e => e.PayFrequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.AmountBasis).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(e => e.Pay);
    }
}
