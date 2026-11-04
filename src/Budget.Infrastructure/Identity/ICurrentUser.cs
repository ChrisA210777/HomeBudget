namespace Budget.Infrastructure.Identity;

/// <summary>Who is making the current request.</summary>
public interface ICurrentUser
{
    /// <summary>The signed-in user's id, or <c>null</c> when nobody is signed in (or for system work).</summary>
    Guid? UserId { get; }
}

/// <summary>
/// The fallback used when the host has not registered its own <see cref="ICurrentUser"/>,
/// e.g. in background work or tools. Changes are recorded as made by the system.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
}
