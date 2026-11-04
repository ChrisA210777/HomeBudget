using Microsoft.AspNetCore.Identity;

namespace Budget.Infrastructure.Identity;

/// <summary>
/// A person who can sign in. Users sign in only with external providers (see ADR-002),
/// so no password hash is ever stored. Keys are GUIDs.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 200;

    /// <summary>The name shown in the app, taken from the external provider at sign-up.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
