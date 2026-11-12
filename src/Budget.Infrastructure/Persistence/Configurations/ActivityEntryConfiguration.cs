using Budget.Domain.Activity;
using Budget.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

/// <summary>Recent activity (BUD-139): one row per change, removed with the household or after three months.</summary>
public sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("ActivityEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ActorName).HasMaxLength(ActivityEntry.ActorNameMaxLength).IsRequired();
        builder.Property(a => a.Area).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Summary).HasMaxLength(ActivityEntry.SummaryMaxLength).IsRequired();
        builder.Property(a => a.MergeKey).HasMaxLength(ActivityEntry.MergeKeyMaxLength);

        // Read newest first for a household.
        builder.HasIndex(a => new { a.HouseholdId, a.At });

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(a => a.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
