using System.Net.Http.Json;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Client.Account;

/// <summary>The signed-in user's settings on the server (BUD-87).</summary>
public interface IUserSettingsApi
{
    Task<UserSettings> GetAsync();

    /// <returns>The settings as saved.</returns>
    Task<UserSettings> UpdateAsync(UpdateUserSettingsRequest request);
}

public sealed class UserSettingsApi(HttpClient http) : IUserSettingsApi
{
    private static readonly Uri Route = new(AccountRoutes.Settings, UriKind.Relative);

    public async Task<UserSettings> GetAsync() =>
        await http.GetFromJsonAsync<UserSettings>(Route) ?? UserSettings.Defaults;

    public async Task<UserSettings> UpdateAsync(UpdateUserSettingsRequest request)
    {
        using var response = await http.PutAsJsonAsync(Route, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserSettings>() ?? UserSettings.Defaults;
    }
}

/// <summary>
/// The user's settings for this visit: read once from the server, then kept here so pages don't wait
/// for them again. A change shows straight away and is saved in the background; if the server can't be
/// reached (e.g. offline), the app carries on with what it has.
/// </summary>
public sealed class UserSettingsStore(IUserSettingsApi api)
{
    private Task<UserSettings>? _loading;

    public UserSettings Current { get; private set; } = UserSettings.Defaults;

    public async Task<UserSettings> GetAsync()
    {
        _loading ??= LoadAsync();
        return await _loading;
    }

    /// <summary>Applies the change now, then saves it.</summary>
    /// <returns><c>false</c> if it couldn't be saved; it still applies until the app is reloaded.</returns>
    public async Task<bool> UpdateAsync(UpdateUserSettingsRequest change)
    {
        ArgumentNullException.ThrowIfNull(change);
        await GetAsync();
        Current = Apply(Current, change);
        _loading = Task.FromResult(Current);
        try
        {
            Current = await api.UpdateAsync(change);
            _loading = Task.FromResult(Current);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<UserSettings> LoadAsync()
    {
        try
        {
            Current = await api.GetAsync();
        }
        catch (HttpRequestException)
        {
            // Offline or signed out: use what we have, and ask again next time.
            _loading = null;
        }

        return Current;
    }

    private static UserSettings Apply(UserSettings settings, UpdateUserSettingsRequest change) => settings with
    {
        FirstDayOfWeek = change.FirstDayOfWeek ?? settings.FirstDayOfWeek,
        OutgoingsPeriod = change.OutgoingsPeriod ?? settings.OutgoingsPeriod,
        OutgoingsGrouping = change.OutgoingsGrouping ?? settings.OutgoingsGrouping,
        IncomePeriod = change.IncomePeriod ?? settings.IncomePeriod,
    };
}
