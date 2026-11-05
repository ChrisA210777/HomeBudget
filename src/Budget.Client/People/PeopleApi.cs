using System.Net;
using System.Net.Http.Json;
using Budget.Shared.People;

namespace Budget.Client.People;

/// <summary>The outcome of saving a person: the saved person, or why it wasn't saved.</summary>
public sealed record SavePersonResult(PersonSummary? Saved, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Saved is not null;
}

public interface IPeopleApi
{
    Task<IReadOnlyList<PersonSummary>> ListAsync(Guid householdId);

    Task<SavePersonResult> AddAsync(Guid householdId, SavePersonRequest request);

    Task<SavePersonResult> RenameAsync(Guid householdId, Guid personId, SavePersonRequest request);
}

public sealed class PeopleApi(HttpClient http) : IPeopleApi
{
    public async Task<IReadOnlyList<PersonSummary>> ListAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<PersonSummary>>(Relative(PeopleRoutes.People(householdId))) ?? [];

    public async Task<SavePersonResult> AddAsync(Guid householdId, SavePersonRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(PeopleRoutes.People(householdId)), request);
        return await ToResultAsync(response);
    }

    public async Task<SavePersonResult> RenameAsync(Guid householdId, Guid personId, SavePersonRequest request)
    {
        using var response = await http.PutAsJsonAsync(Relative(PeopleRoutes.Person(householdId, personId)), request);
        return await ToResultAsync(response);
    }

    private static async Task<SavePersonResult> ToResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return new SavePersonResult(await response.Content.ReadFromJsonAsync<PersonSummary>(), new Dictionary<string, string[]>());
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return new SavePersonResult(null, problem?.Errors ?? []);
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
