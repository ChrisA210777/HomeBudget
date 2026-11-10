using Budget.Shared.Account;

namespace Budget.Client.Account;

/// <summary>
/// The signed-in user and their households, fetched once from the server and shared by the
/// authentication state and the pages that need it.
/// </summary>
public sealed class UserSession(IAccountApi accountApi)
{
    private Task<CurrentUserResponse?>? _currentUser;

    /// <summary>Raised when the cached user or households change, e.g. after a rename.</summary>
    public event EventHandler? Changed;

    /// <returns>The signed-in user, or <c>null</c> if nobody is signed in.</returns>
    public Task<CurrentUserResponse?> GetCurrentUserAsync()
    {
        // A failed request (e.g. while offline) isn't kept, so the next call tries again.
        if (_currentUser is null || _currentUser.IsFaulted || _currentUser.IsCanceled)
        {
            _currentUser = accountApi.GetCurrentUserAsync();
        }

        return _currentUser;
    }

    /// <returns>The household the user is working in, or <c>null</c> if they have none.</returns>
    public async Task<HouseholdMembershipSummary?> GetActiveHouseholdAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.Households.FirstOrDefault(h => h.HouseholdId == user.LastActiveHouseholdId);
    }

    /// <summary>Updates the cached name of the user's own person after they rename it, so the app bar shows it.</summary>
    public async Task SetPersonNameAsync(Guid householdId, string name)
    {
        if (await GetCurrentUserAsync() is not { } user)
        {
            return;
        }

        var households = user.Households
            .Select(h => h.HouseholdId == householdId ? h with { PersonName = name } : h)
            .ToList();
        _currentUser = Task.FromResult<CurrentUserResponse?>(user with { Households = households });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Updates the cached "Accounts and transfers" setting (BUD-91), so menus show or hide transfers.</summary>
    public async Task SetTransfersEnabledAsync(Guid householdId, bool enabled)
    {
        if (await GetCurrentUserAsync() is not { } user)
        {
            return;
        }

        var households = user.Households
            .Select(h => h.HouseholdId == householdId ? h with { TransfersEnabled = enabled } : h)
            .ToList();
        _currentUser = Task.FromResult<CurrentUserResponse?>(user with { Households = households });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Updates the cached household name after a rename, so every page shows the new name.</summary>
    public async Task SetHouseholdNameAsync(Guid householdId, string name)
    {
        if (await GetCurrentUserAsync() is not { } user)
        {
            return;
        }

        var households = user.Households
            .Select(h => h.HouseholdId == householdId ? h with { Name = name } : h)
            .ToList();
        _currentUser = Task.FromResult<CurrentUserResponse?>(user with { Households = households });
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
