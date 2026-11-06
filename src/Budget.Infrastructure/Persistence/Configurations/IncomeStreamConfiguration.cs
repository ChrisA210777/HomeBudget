using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

/// <summary>
/// An income stream and its timeline (see ADR-009). The amount changes, status periods and
/// corrections are only reached through their stream, which carries the household query filter.
/// </summary>
public sealed class IncomeStreamConfiguration : IEntityTypeConfiguration<IncomeStream>
{
    /// <summary>Shadow property for optimistic concurrency (SQL Server rowversion).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<IncomeStream> builder)
    {
        builder.ToTable("IncomeStreams");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(IncomeStream.NameMaxLength).IsRequired();
        builder.Property<byte[]>(RowVersion).IsRowVersion();
        builder.HasIndex(s => s.HouseholdId);
        builder.Ignore(s => s.StartsOn);
        builder.Ignore(s => s.EndsOn);

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(s => s.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // People can't be removed yet; when they can, their income will be dealt with in code.
        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(s => s.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.AmountChanges).WithOne().HasForeignKey(c => c.IncomeStreamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.StatusPeriods).WithOne().HasForeignKey(p => p.IncomeStreamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Corrections).WithOne().HasForeignKey(c => c.IncomeStreamId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.AmountChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(s => s.StatusPeriods).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(s => s.Corrections).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class IncomeAmountChangeConfiguration : IEntityTypeConfiguration<IncomeAmountChange>
{
    public void Configure(EntityTypeBuilder<IncomeAmountChange> builder)
    {
        builder.ToTable("IncomeAmountChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Comment).HasMaxLength(IncomeStream.CommentMaxLength).IsRequired();
        builder.Property(c => c.Unit).HasConversion<string>().HasMaxLength(10);
        builder.Ignore(c => c.Schedule);
        builder.Ignore(c => c.MonthlyEquivalent);

        // One amount change per stream per date.
        builder.HasIndex(c => new { c.IncomeStreamId, c.EffectiveFrom }).IsUnique();
    }
}

public sealed class IncomeStatusPeriodConfiguration : IEntityTypeConfiguration<IncomeStatusPeriod>
{
    public void Configure(EntityTypeBuilder<IncomeStatusPeriod> builder)
    {
        builder.ToTable("IncomeStatusPeriods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Comment).HasMaxLength(IncomeStream.CommentMaxLength).IsRequired();
        builder.HasIndex(p => p.IncomeStreamId);
    }
}

public sealed class IncomeCorrectionConfiguration : IEntityTypeConfiguration<IncomeCorrection>
{
    public void Configure(EntityTypeBuilder<IncomeCorrection> builder)
    {
        builder.ToTable("IncomeCorrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.EntryType).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Before).HasMaxLength(IncomeCorrection.DescriptionMaxLength).IsRequired();
        builder.Property(c => c.After).HasMaxLength(IncomeCorrection.DescriptionMaxLength);
        builder.Property(c => c.Reason).HasMaxLength(IncomeStream.CommentMaxLength).IsRequired();
        builder.HasIndex(c => c.IncomeStreamId);
    }
}
