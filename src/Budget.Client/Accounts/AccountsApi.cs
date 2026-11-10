using System.Net;
using System.Net.Http.Json;
using Budget.Shared.Accounts;

namespace Budget.Client.Accounts;

/// <summary>The outcome of a change: the household's accounts afterwards, or why it wasn't made.</summary>
public sealed record AccountsResult(IReadOnlyList<AccountSummary>? Accounts, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Accounts is not null;

    /// <summary>The first problem, to show beside the form.</summary>
    public string? Problem => Errors.Values.SelectMany(e => e).FirstOrDefault();
}

/// <summary>The household's accounts and pots (BUD-89).</summary>
public interface IAccountsApi
{
    Task<IReadOnlyList<AccountSummary>> ListAsync(Guid householdId);

    Task<AccountsResult> AddAsync(Guid householdId, SaveAccountRequest request);

    Task<AccountsResult> UpdateAsync(Guid householdId, Guid accountId, SaveAccountRequest request);

    Task<AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived);

    Task<AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId);
}

public sealed class AccountsApi(HttpClient http) : IAccountsApi
{
    public async Task<IReadOnlyList<AccountSummary>> ListAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<AccountSummary>>(Relative(HouseholdAccountRoutes.Accounts(householdId))) ?? [];

    public async Task<AccountsResult> AddAsync(Guid householdId, SaveAccountRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(HouseholdAccountRoutes.Accounts(householdId)), request);
        return await ThenListAsync(householdId, response);
    }

    public async Task<AccountsResult> UpdateAsync(Guid householdId, Guid accountId, SaveAccountRequest request)
    {
        using var response = await http.PutAsJsonAsync(Relative(HouseholdAccountRoutes.Account(householdId, accountId)), request);
        return await ThenListAsync(householdId, response);
    }

    public async Task<AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived)
    {
        var route = archived ? HouseholdAccountRoutes.Archive(householdId, accountId) : HouseholdAccountRoutes.Restore(householdId, accountId);
        using var response = await http.PostAsync(Relative(route), content: null);
        return await ThenListAsync(householdId, response);
    }

    public async Task<AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId)
    {
        using var response = await http.PostAsJsonAsync(Relative(HouseholdAccountRoutes.Merge(householdId, accountId)), new MergeAccountRequest { IntoAccountId = intoAccountId });
        return await ThenListAsync(householdId, response);
    }

    /// <summary>On success, the whole list again (a change can move pots and outgoings around).</summary>
    private async Task<AccountsResult> ThenListAsync(Guid householdId, HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return new AccountsResult(await ListAsync(householdId), new Dictionary<string, string[]>());
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return new AccountsResult(null, problem?.Errors ?? []);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
