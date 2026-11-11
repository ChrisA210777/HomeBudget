using Budget.Domain.Households;
using Budget.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Budget.Infrastructure.Persistence.Configurations;

/// <summary>Invitations to join a household (ADR-015). Found by the hash of their link's token.</summary>
public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Email).HasMaxLength(Invitation.EmailMaxLength);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.ProtectedToken).HasMaxLength(1000).IsRequired();
        builder.Property(i => i.InvitedByName).HasMaxLength(Invitation.NameMaxLength).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.HouseholdId, i.SentAt });

        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(i => i.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        // People can't be deleted while linked; an invitation for someone removed just becomes "someone new".
        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(i => i.PersonId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
