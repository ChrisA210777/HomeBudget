using System.Net.Mail;
using System.Security.Cryptography;
using Budget.Domain.Common;

namespace Budget.Domain.Households;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

/// <summary>
/// An invitation to join a household (ADR-015, BUD-23). It's reached by a single-use link that expires after
/// <see cref="Lifetime"/>; only a hash of the link's token is stored for finding it. The invitee's email is
/// kept only while it's pending.
/// </summary>
public sealed class Invitation : AuditableEntity
{
    public const int EmailMaxLength = 256;
    public const int NameMaxLength = 200;
    public const int MaxPerDay = 20;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    /// <summary>Who it was sent to. Cleared once it's accepted, revoked or expired.</summary>
    public string? Email { get; private set; }

    public HouseholdRole Role { get; private set; }

    /// <summary>The Person they'll be (BUD-44), or <c>null</c> for someone new.</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>Only someone signed in with <see cref="Email"/> can accept.</summary>
    public bool RequireEmailMatch { get; private set; }

    /// <summary>SHA-256 of the link's token, in hex, for finding the invitation from its link.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>The token, protected so only the app can read it, so Admins can share the link again.</summary>
    public string ProtectedToken { get; private set; } = string.Empty;

    public Guid InvitedByUserId { get; private set; }

    public string InvitedByName { get; private set; } = string.Empty;

    public DateTimeOffset SentAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// A new invitation and the token for its link. <paramref name="protect"/> protects the token for storing,
    /// so it can be shown again; the token itself isn't kept.
    /// </summary>
    public static (Invitation Invitation, string Token) Create(
        Guid householdId,
        string email,
        HouseholdRole role,
        Guid? personId,
        bool requireEmailMatch,
        Guid invitedByUserId,
        string invitedByName,
        DateTimeOffset now,
        Func<string, string> protect)
    {
        ArgumentNullException.ThrowIfNull(protect);
        if (householdId == Guid.Empty || invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException("An invitation needs a household and who sent it.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "Choose Viewer, User or Admin.");
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Email = NormaliseEmail(email),
            Role = role,
            PersonId = personId,
            RequireEmailMatch = requireEmailMatch,
            TokenHash = Hash(token),
            ProtectedToken = protect(token),
            InvitedByUserId = invitedByUserId,
            InvitedByName = Values.Optional(invitedByName, nameof(invitedByName), NameMaxLength) ?? "Someone",
            SentAt = now,
            ExpiresAt = now + Lifetime,
        };
        return (invitation, token);
    }

    /// <summary>The hash stored for a link's token.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty)));

    /// <summary>A trimmed, valid email address, or an <see cref="ArgumentException"/>.</summary>
    public static string NormaliseEmail(string email)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > EmailMaxLength || !MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed)
        {
            throw new ArgumentException("Enter a valid email address.", nameof(email));
        }

        return trimmed;
    }

    public InvitationStatus StatusAt(DateTimeOffset now) =>
        AcceptedAt is not null ? InvitationStatus.Accepted
        : RevokedAt is not null ? InvitationStatus.Revoked
        : now >= ExpiresAt ? InvitationStatus.Expired
        : InvitationStatus.Pending;

    /// <summary>Whether <paramref name="email"/> may accept: always, unless the invitation asks for a match.</summary>
    public bool AllowsEmail(string? email) =>
        !RequireEmailMatch || (Email is not null && string.Equals(Email, email?.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Accept(Guid userId, DateTimeOffset now)
    {
        EnsurePending(now);
        AcceptedAt = now;
        AcceptedByUserId = userId;
        Email = null;
    }

    public void Revoke(DateTimeOffset now)
    {
        EnsurePending(now);
        RevokedAt = now;
        Email = null;
    }

    /// <summary>Forgets the email of an invitation that has expired (ADR-015).</summary>
    public void ForgetEmailIfExpired(DateTimeOffset now)
    {
        if (StatusAt(now) == InvitationStatus.Expired)
        {
            Email = null;
        }
    }

    private void EnsurePending(DateTimeOffset now)
    {
        var status = StatusAt(now);
        if (status != InvitationStatus.Pending)
        {
            throw new InvalidOperationException(status switch
            {
                InvitationStatus.Accepted => "This invitation has already been used.",
                InvitationStatus.Revoked => "This invitation was cancelled.",
                _ => "This invitation has expired.",
            });
        }
    }
}
