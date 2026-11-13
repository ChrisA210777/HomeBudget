using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.People;
using Budget.Infrastructure.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Ignore(a => a.IsPot);

        builder.Property(a => a.Name).HasMaxLength(Account.NameMaxLength).IsRequired().IsEncrypted();
        builder.Property(a => a.Provider).HasMaxLength(Account.ProviderMaxLength).IsEncrypted();

        // The owner is a person in the household; people are never deleted while they own anything.
        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(a => a.OwnerPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Pots' names are unique within their account, ignoring case; accounts' names are checked by the app (as before,
        // the index leaves out rows without a parent). Names are encrypted, so the index is on their keyed hash (BUD-166).
        // Rows saved before BUD-166 get their key on start-up.
        builder.HasNameKey(a => a.Name);
        builder.HasIndex(nameof(Account.HouseholdId), nameof(Account.ParentAccountId), EncryptionModelExtensions.NameKeyColumn)
            .IsUnique().HasFilter("[ParentAccountId] IS NOT NULL AND [NameKey] IS NOT NULL");

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(a => a.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // Pots are archived with their account, never deleted on their own.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(a => a.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
