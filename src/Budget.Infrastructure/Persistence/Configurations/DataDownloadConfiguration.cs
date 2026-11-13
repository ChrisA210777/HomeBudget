using Budget.Domain.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

/// <summary>The data download log (BUD-141): one row per download, removed with the household or after twelve months.</summary>
public sealed class DataDownloadConfiguration : IEntityTypeConfiguration<DataDownload>
{
    public void Configure(EntityTypeBuilder<DataDownload> builder)
    {
        builder.ToTable("DataDownloads");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Format).HasConversion<string>().HasMaxLength(10);

        // Read newest first for a household.
        builder.HasIndex(d => new { d.HouseholdId, d.At });

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(d => d.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
