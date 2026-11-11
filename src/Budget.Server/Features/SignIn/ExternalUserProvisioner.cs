using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.SignIn;

/// <summary>
/// Finds the user linked to an external login. The first time someone signs in it creates their
/// user, links the login, creates "My Household" with them as Owner and Admin, and adds them as a
/// person in it, all in one transaction: either everything is created or nothing is.
/// </summary>
public sealed partial class ExternalUserProvisioner(
    BudgetDbContext db,
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

        // One user per email (ADR-015): a login whose provider has verified the email joins the user who already
        // has it. Only verified emails count, so nobody can claim someone else's account with an unchecked address.
        if (VerifiedEmail(login) is { } email && await OnlyUserWithEmailAsync(email) is { } sameEmail)
        {
            var added = await users.AddLoginAsync(sameEmail, new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName));
            if (added.Succeeded)
            {
                LogLoginAdded(logger, sameEmail.Id, login.LoginProvider);
                return sameEmail;
            }

            LogIdentityFailure(logger, "add login to the user with the same email", login.LoginProvider, Describe(added));
            return null;
        }

        try
        {
            // Connections to Azure SQL are retried on transient failures, so a transaction must run
            // inside the execution strategy, which re-runs the whole block if it has to retry.
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(() => CreateUserWithHouseholdAsync(login));
        }
        catch (DbUpdateException ex)
        {
            LogFailure(logger, ex, login.LoginProvider);
            return null;
        }
    }

    private async Task<ApplicationUser?> CreateUserWithHouseholdAsync(ExternalLoginInfo login)
    {
        // Start clean in case this is a retry after a transient failure.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var now = timeProvider.GetUtcNow();
        var user = NewUser(login, now);
        var household = Household.Create(user.Id, now);
        user.LastActiveHouseholdId = household.Id;

        // CreateAsync saves the household, its owner membership, the owner's person and the default
        // outgoing categories along with the user.
        db.Households.Add(household);
        db.People.Add(Person.CreateForUser(household.Id, user.Id, user.DisplayName));
        db.OutgoingCategories.AddRange(OutgoingCategory.CreateDefaults(household.Id));

        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            LogIdentityFailure(logger, "create user", login.LoginProvider, Describe(created));
            return null;
        }

        var linked = await users.AddLoginAsync(user, new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName));
        if (!linked.Succeeded)
        {
            LogIdentityFailure(logger, "link external login", login.LoginProvider, Describe(linked));
            return null;
        }

        await transaction.CommitAsync();

        LogUserCreated(logger, user.Id, household.Id, login.LoginProvider);
        return user;
    }

    private static ApplicationUser NewUser(ExternalLoginInfo login, DateTimeOffset now)
    {
        var email = login.Principal.FindFirstValue(ClaimTypes.Email);
        var displayName = login.Principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email ?? "New user";
        }

        return new ApplicationUser
        {
            Id = Guid.CreateVersion7(),

            // The user name is internal only; people are identified by their external login.
            UserName = Guid.NewGuid().ToString("N"),
            Email = email,
            DisplayName = displayName.Length > ApplicationUser.DisplayNameMaxLength
                ? displayName[..ApplicationUser.DisplayNameMaxLength]
                : displayName,
            CreatedAt = now,
        };
    }

    /// <summary>The user with this email, unless there's none or (from before this rule) more than one.</summary>
    private async Task<ApplicationUser?> OnlyUserWithEmailAsync(string email)
    {
        try
        {
            return await users.FindByEmailAsync(email);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The login's email, if its provider says it has verified it.</summary>
    private static string? VerifiedEmail(ExternalLoginInfo login)
    {
        var email = login.Principal.FindFirstValue(ClaimTypes.Email);
        var verified = login.Principal.FindFirstValue(AuthenticationSetup.EmailVerifiedClaim);
        return !string.IsNullOrWhiteSpace(email) && string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase) ? email : null;
    }

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Code));

    [LoggerMessage(Level = LogLevel.Information, Message = "Created user {UserId} with household {HouseholdId} on first sign-in with {Provider}")]
    private static partial void LogUserCreated(ILogger logger, Guid userId, Guid householdId, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added a {Provider} login to user {UserId}, who has the same verified email")]
    private static partial void LogLoginAdded(ILogger logger, Guid userId, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not {Action} for sign-in with {Provider}: {Errors}")]
    private static partial void LogIdentityFailure(ILogger logger, string action, string provider, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save the new user for sign-in with {Provider}")]
    private static partial void LogFailure(ILogger logger, Exception exception, string provider);
}
