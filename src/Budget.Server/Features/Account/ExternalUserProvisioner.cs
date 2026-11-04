using System.Security.Claims;
using Budget.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Budget.Server.Features.Account;

/// <summary>
/// Finds the user linked to an external login, creating one the first time someone signs in.
/// </summary>
public sealed partial class ExternalUserProvisioner(
    UserManager<ApplicationUser> users,
    TimeProvider timeProvider,
    ILogger<ExternalUserProvisioner> logger)
{
    /// <returns>The user, or <c>null</c> if a new user could not be created.</returns>
    public async Task<ApplicationUser?> FindOrCreateAsync(ExternalLoginInfo login)
    {
        ArgumentNullException.ThrowIfNull(login);

        var existing = await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey);
        if (existing is not null)
        {
            return existing;
        }

        var email = login.Principal.FindFirstValue(ClaimTypes.Email);
        var displayName = login.Principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email ?? "New user";
        }

        var user = new ApplicationUser
        {
            // The user name is internal only; people are identified by their external login.
            UserName = Guid.NewGuid().ToString("N"),
            Email = email,
            DisplayName = displayName.Length > ApplicationUser.DisplayNameMaxLength
                ? displayName[..ApplicationUser.DisplayNameMaxLength]
                : displayName,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            LogFailure(logger, "create user", login.LoginProvider, Describe(created));
            return null;
        }

        var linked = await users.AddLoginAsync(user, new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName));
        if (!linked.Succeeded)
        {
            LogFailure(logger, "link external login", login.LoginProvider, Describe(linked));
            await users.DeleteAsync(user);
            return null;
        }

        LogUserCreated(logger, user.Id, login.LoginProvider);
        return user;
    }

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Code));

    [LoggerMessage(Level = LogLevel.Information, Message = "Created user {UserId} on first sign-in with {Provider}")]
    private static partial void LogUserCreated(ILogger logger, Guid userId, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not {Action} for sign-in with {Provider}: {Errors}")]
    private static partial void LogFailure(ILogger logger, string action, string provider, string errors);
}
