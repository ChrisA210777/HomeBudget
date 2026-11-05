using System.Net;
using System.Net.Http.Json;
using Budget.Shared.Households;

namespace Budget.Client.Households;

/// <summary>
/// The outcome of a rename: the renamed household, field errors, or <see cref="CurrentName"/> when
/// someone else renamed it first.
/// </summary>
public sealed record RenameHouseholdResult(HouseholdResponse? Household, IReadOnlyDictionary<string, string[]> Errors, string? CurrentName = null)
{
    public bool Succeeded => Household is not null;

    public bool Conflict => CurrentName is not null;
}

public interface IHouseholdApi
{
    Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request);
}

public sealed class HouseholdApi(HttpClient http) : IHouseholdApi
{
    public async Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request)
    {
        var url = new Uri(HouseholdRoutes.Household(householdId), UriKind.Relative);
        using var response = await http.PutAsJsonAsync(url, request);

        if (response.IsSuccessStatusCode)
        {
            return new RenameHouseholdResult(await response.Content.ReadFromJsonAsync<HouseholdResponse>(), new Dictionary<string, string[]>());
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return new RenameHouseholdResult(null, problem?.Errors ?? []);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var current = await http.GetFromJsonAsync<HouseholdResponse>(url);
            return new RenameHouseholdResult(null, new Dictionary<string, string[]>(), current!.Name);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
