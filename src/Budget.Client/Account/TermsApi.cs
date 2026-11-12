using System.Net;
using System.Net.Http.Json;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Client.Account;

/// <summary>Accepting the terms of use (BUD-152).</summary>
public interface ITermsApi
{
    /// <returns><c>null</c> once accepted, otherwise why not (e.g. the terms have changed since the page loaded).</returns>
    Task<string?> AcceptAsync(string version);
}

public sealed class TermsApi(HttpClient http) : ITermsApi
{
    public async Task<string?> AcceptAsync(string version)
    {
        using var response = await http.PostAsJsonAsync(new Uri(AccountRoutes.AcceptTerms, UriKind.Relative), new AcceptTermsRequest { Version = version });
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>();
            return problem?.Errors?.Values.SelectMany(v => v).FirstOrDefault() ?? "The terms couldn't be accepted.";
        }

        response.EnsureSuccessStatusCode();
        return null;
    }

    private sealed record Problem(Dictionary<string, string[]>? Errors);
}
