using System.Net;
using System.Net.Http.Json;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Client.Account;

/// <summary>Deleting the signed-in user's account (BUD-151).</summary>
public interface IAccountDeletionApi
{
    /// <summary>What deleting the account would do, and whether households must be handed on first.</summary>
    Task<AccountDeletionCheck> CheckAsync();

    /// <summary>Deletes the account and signs out.</summary>
    /// <returns><c>null</c> when it's deleted, otherwise why it wasn't.</returns>
    Task<string?> DeleteAsync(string confirmation);
}

public sealed class AccountDeletionApi(HttpClient http) : IAccountDeletionApi
{
    public async Task<AccountDeletionCheck> CheckAsync() =>
        await http.GetFromJsonAsync<AccountDeletionCheck>(new Uri(AccountRoutes.AccountDeletion, UriKind.Relative))
        ?? throw new InvalidOperationException("The server didn't say what deleting the account would do.");

    public async Task<string?> DeleteAsync(string confirmation)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri(AccountRoutes.DeleteAccount, UriKind.Relative), new DeleteAccountRequest { Confirmation = confirmation });
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
            return problem?.Detail ?? problem?.Title ?? "Your account couldn't be deleted.";
        }

        response.EnsureSuccessStatusCode();
        return null;
    }

    private sealed record ProblemResponse(string? Title, string? Detail);
}
