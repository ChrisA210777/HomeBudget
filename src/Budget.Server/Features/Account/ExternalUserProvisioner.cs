using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Account;

/// <summary>
/// Finds the user linked to an external login. The first time someone signs in it creates their
/// user, links the login and creates "My Household" with them as Owner and Admin, all in one
/// transaction: either everything is created or nothing is.
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

        // CreateAsync saves the household and its owner membership along with the user.
        db.Households.Add(household);

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

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Code));

    [LoggerMessage(Level = LogLevel.Information, Message = "Created user {UserId} with household {HouseholdId} on first sign-in with {Provider}")]
    private static partial void LogUserCreated(ILogger logger, Guid userId, Guid householdId, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not {Action} for sign-in with {Provider}: {Errors}")]
    private static partial void LogIdentityFailure(ILogger logger, string action, string provider, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save the new user for sign-in with {Provider}")]
    private static partial void LogFailure(ILogger logger, Exception exception, string provider);
}
