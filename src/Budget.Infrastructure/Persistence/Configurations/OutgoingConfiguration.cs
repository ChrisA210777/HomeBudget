using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

public sealed class OutgoingCategoryConfiguration : IEntityTypeConfiguration<OutgoingCategory>
{
    public void Configure(EntityTypeBuilder<OutgoingCategory> builder)
    {
        builder.ToTable("OutgoingCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(OutgoingCategory.NameMaxLength).IsRequired();
        builder.Property(c => c.Icon).HasMaxLength(OutgoingCategory.IconMaxLength).IsUnicode(false).IsRequired()
            .HasDefaultValue(OutgoingCategory.DefaultIcon);

        // Unique within a household; the collation is case-insensitive.
        builder.HasIndex(c => new { c.HouseholdId, c.Name }).IsUnique();

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(c => c.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// An outgoing and its timeline (ADR-011). Its amount changes, status periods and corrections are
/// only reached through it, and it carries the household query filter.
/// </summary>
public sealed class OutgoingConfiguration : IEntityTypeConfiguration<Outgoing>
{
    /// <summary>Shadow property for optimistic concurrency (SQL Server rowversion).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Outgoing> builder)
    {
        builder.ToTable("Outgoings");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property<byte[]>(RowVersion).IsRowVersion();
        builder.HasIndex(o => o.HouseholdId);
        builder.Ignore(o => o.StartsOn);
        builder.Ignore(o => o.EndsOn);

        builder.Property(o => o.Name).HasMaxLength(Outgoing.NameMaxLength).IsRequired();
        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.AlertPeriod).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Recipient).HasMaxLength(Outgoing.RecipientMaxLength);
        builder.Property(o => o.ReferenceLabel).HasMaxLength(Outgoing.ReferenceLabelMaxLength);
        builder.Property(o => o.ReferenceValue).HasMaxLength(Outgoing.ReferenceValueMaxLength);
        builder.Property(o => o.Website).HasMaxLength(Outgoing.WebsiteMaxLength);
        builder.Property(o => o.ContactPhone).HasMaxLength(Outgoing.ContactPhoneMaxLength);
        builder.Property(o => o.Notes).HasMaxLength(Outgoing.NotesMaxLength);

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(o => o.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Accounts are archived, never deleted (ADR-014).
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(o => o.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // A category in use can't be deleted (BUD-47).
        builder.HasOne<OutgoingCategory>()
            .WithMany()
            .HasForeignKey(o => o.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(o => o.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.AmountChanges).WithOne().HasForeignKey(c => c.OutgoingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.StatusPeriods).WithOne().HasForeignKey(p => p.OutgoingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Corrections).WithOne().HasForeignKey(c => c.OutgoingId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.AmountChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusPeriods).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Corrections).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OutgoingAmountChangeConfiguration : IEntityTypeConfiguration<OutgoingAmountChange>
{
    public void Configure(EntityTypeBuilder<OutgoingAmountChange> builder)
    {
        builder.ToTable("OutgoingAmountChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Unit).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Comment).HasMaxLength(Outgoing.CommentMaxLength).IsRequired();
        builder.Ignore(c => c.Schedule);
        builder.Ignore(c => c.MonthlyEquivalent);

        // One amount change per outgoing per date.
        builder.HasIndex(c => new { c.OutgoingId, c.EffectiveFrom }).IsUnique();
    }
}

public sealed class OutgoingStatusPeriodConfiguration : IEntityTypeConfiguration<OutgoingStatusPeriod>
{
    public void Configure(EntityTypeBuilder<OutgoingStatusPeriod> builder)
    {
        builder.ToTable("OutgoingStatusPeriods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Comment).HasMaxLength(Outgoing.CommentMaxLength).IsRequired();
        builder.HasIndex(p => p.OutgoingId);
    }
}

public sealed class OutgoingCorrectionConfiguration : IEntityTypeConfiguration<OutgoingCorrection>
{
    public void Configure(EntityTypeBuilder<OutgoingCorrection> builder)
    {
        builder.ToTable("OutgoingCorrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.EntryType).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Before).HasMaxLength(OutgoingCorrection.DescriptionMaxLength).IsRequired();
        builder.Property(c => c.After).HasMaxLength(OutgoingCorrection.DescriptionMaxLength);
        builder.Property(c => c.Reason).HasMaxLength(Outgoing.CommentMaxLength).IsRequired();
        builder.HasIndex(c => c.OutgoingId);
    }
}
