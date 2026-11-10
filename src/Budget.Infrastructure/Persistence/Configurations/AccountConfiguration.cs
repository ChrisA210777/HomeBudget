using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.People;
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

        builder.Property(a => a.Name).HasMaxLength(Account.NameMaxLength).IsRequired();
        builder.Property(a => a.Provider).HasMaxLength(Account.ProviderMaxLength);

        // The owner is a person in the household; people are never deleted while they own anything.
        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(a => a.OwnerPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Names are unique among a household's accounts and among an account's pots. The collation ignores case,
        // and a null parent counts as a value in a SQL Server unique index, so accounts are covered too.
        builder.HasIndex(a => new { a.HouseholdId, a.ParentAccountId, a.Name }).IsUnique();

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
