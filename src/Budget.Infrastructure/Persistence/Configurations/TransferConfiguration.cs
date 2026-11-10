using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

/// <summary>
/// A transfer between accounts and its timeline (ADR-014, BUD-91). Its amount changes, status periods and
/// corrections are only reached through it, and it carries the household query filter.
/// </summary>
public sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    /// <summary>Shadow property for optimistic concurrency (SQL Server rowversion).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("Transfers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property<byte[]>(RowVersion).IsRowVersion();
        builder.HasIndex(t => t.HouseholdId);
        builder.Ignore(t => t.StartsOn);
        builder.Ignore(t => t.EndsOn);

        builder.Property(t => t.Name).HasMaxLength(Transfer.NameMaxLength);
        builder.Property(t => t.Notes).HasMaxLength(Transfer.NotesMaxLength);

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(t => t.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Accounts are archived, never deleted (ADR-014).
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.FromAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.ToAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.AmountChanges).WithOne().HasForeignKey(c => c.TransferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.StatusPeriods).WithOne().HasForeignKey(p => p.TransferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Corrections).WithOne().HasForeignKey(c => c.TransferId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.AmountChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.StatusPeriods).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Corrections).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TransferAmountChangeConfiguration : IEntityTypeConfiguration<TransferAmountChange>
{
    public void Configure(EntityTypeBuilder<TransferAmountChange> builder)
    {
        builder.ToTable("TransferAmountChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Unit).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Comment).HasMaxLength(Transfer.CommentMaxLength).IsRequired();
        builder.Ignore(c => c.Schedule);
        builder.Ignore(c => c.MonthlyEquivalent);

        // One amount change per transfer per date.
        builder.HasIndex(c => new { c.TransferId, c.EffectiveFrom }).IsUnique();
    }
}

public sealed class TransferStatusPeriodConfiguration : IEntityTypeConfiguration<TransferStatusPeriod>
{
    public void Configure(EntityTypeBuilder<TransferStatusPeriod> builder)
    {
        builder.ToTable("TransferStatusPeriods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Comment).HasMaxLength(Transfer.CommentMaxLength).IsRequired();
        builder.HasIndex(p => p.TransferId);
    }
}

public sealed class TransferCorrectionConfiguration : IEntityTypeConfiguration<TransferCorrection>
{
    public void Configure(EntityTypeBuilder<TransferCorrection> builder)
    {
        builder.ToTable("TransferCorrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.EntryType).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Before).HasMaxLength(TransferCorrection.DescriptionMaxLength).IsRequired();
        builder.Property(c => c.After).HasMaxLength(TransferCorrection.DescriptionMaxLength);
        builder.Property(c => c.Reason).HasMaxLength(Transfer.CommentMaxLength).IsRequired();
        builder.HasIndex(c => c.TransferId);
    }
}
