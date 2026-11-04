using Budget.Shared.Account;

namespace Budget.Client.Account;

/// <summary>
/// The signed-in user and their households, fetched once from the server and shared by the
/// authentication state and the pages that need it.
/// </summary>
public sealed class UserSession(IAccountApi accountApi)
{
    private Task<CurrentUserResponse?>? _currentUser;

    /// <returns>The signed-in user, or <c>null</c> if nobody is signed in.</returns>
    public Task<CurrentUserResponse?> GetCurrentUserAsync() => _currentUser ??= accountApi.GetCurrentUserAsync();

    /// <returns>The household the user is working in, or <c>null</c> if they have none.</returns>
    public async Task<HouseholdMembershipSummary?> GetActiveHouseholdAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.Households.FirstOrDefault(h => h.HouseholdId == user.LastActiveHouseholdId);
    }
}
