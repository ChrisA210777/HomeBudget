namespace Budget.Shared.Households;

/// <summary>Invitations to join a household (ADR-015, BUD-23 to BUD-25).</summary>
public static class InvitationRoutes
{
    /// <summary>GET (Admins): pending invitations. POST (Admins): invite someone.</summary>
    public static string Invitations(Guid householdId) => $"/api/households/{householdId}/invitations";

    /// <summary>DELETE (Admins): revoke an invitation.</summary>
    public static string Invitation(Guid householdId, Guid invitationId) => $"{Invitations(householdId)}/{invitationId}";

    /// <summary>GET (signed in): what an invitation link is for.</summary>
    public static string ByToken(string token) => $"/api/invitations/{Uri.EscapeDataString(token)}";

    /// <summary>POST (signed in): accept it.</summary>
    public static string Accept(string token) => $"{ByToken(token)}/accept";

    /// <summary>The page an invitation link opens.</summary>
    public static string Page(string token) => $"/invite/{Uri.EscapeDataString(token)}";
}

public sealed record CreateInvitationRequest
{
    public string? Email { get; init; }

    /// <summary>Viewer, User or Admin.</summary>
    public string Role { get; init; } = "User";

    /// <summary>The Person they'll be, or <c>null</c> for someone new (BUD-44).</summary>
    public Guid? PersonId { get; init; }

    /// <summary>Only someone signed in with <see cref="Email"/> can accept.</summary>
    public bool RequireEmailMatch { get; init; }
}

/// <summary>A pending invitation, with its link for sharing again.</summary>
/// <param name="Link">The full link, or <c>null</c> if it can no longer be shown (revoke it and invite again).</param>
public sealed record InvitationSummary(
    Guid Id,
    string Email,
    string Role,
    Guid? PersonId,
    string? PersonName,
    bool RequireEmailMatch,
    string InvitedByName,
    DateTimeOffset ExpiresAt,
    string? Link);

/// <summary>What an invitation link is for, as seen by the person who opened it.</summary>
/// <param name="Status">Pending, Accepted, Revoked or Expired.</param>
/// <param name="EmailAllowed">False when it needs a different email from the one they're signed in with.</param>
/// <param name="InvitedEmail">Shown only when the email has to match, so they know which account to use.</param>
public sealed record InvitationPreview(
    string HouseholdName,
    string InvitedByName,
    string Role,
    string? PersonName,
    DateTimeOffset ExpiresAt,
    string Status,
    bool RequireEmailMatch,
    bool EmailAllowed,
    string? InvitedEmail,
    string? SignedInEmail,
    bool AlreadyMember);

public sealed record AcceptInvitationResponse(Guid HouseholdId, string HouseholdName);

/// <summary>Wording for sharing an invitation, shared by the app's Share, Copy and Email options.</summary>
public static class InvitationText
{
    public static string Subject(string householdName) => $"Join {householdName} on Home Budget";

    public static string Body(string inviterName, string householdName, string link, DateTimeOffset expiresAt) =>
        $"{inviterName} has invited you to {householdName} on Home Budget. Open this link to join (it expires on {expiresAt:d MMMM}):\n\n{link}";

    /// <summary>A mailto: link that opens the person's own email app with the invitation filled in.</summary>
    public static string MailTo(string email, string inviterName, string householdName, string link, DateTimeOffset expiresAt) =>
        $"mailto:{Uri.EscapeDataString(email)}?subject={Uri.EscapeDataString(Subject(householdName))}&body={Uri.EscapeDataString(Body(inviterName, householdName, link, expiresAt))}";
}
