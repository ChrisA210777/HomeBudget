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

    /// <summary>What removing the person would affect (Admins, BUD-112).</summary>
    Task<PersonRemovalCheck> CheckRemovalAsync(Guid householdId, Guid personId);

    /// <summary>Removes the person, moving or deleting their items as chosen (Admins, BUD-112).</summary>
    /// <returns><c>null</c> when removed, otherwise why not.</returns>
    Task<string?> RemoveAsync(Guid householdId, Guid personId, RemovePersonRequest request);
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

    public async Task<PersonRemovalCheck> CheckRemovalAsync(Guid householdId, Guid personId) =>
        await http.GetFromJsonAsync<PersonRemovalCheck>(Relative(PeopleRoutes.Removal(householdId, personId)))
        ?? throw new InvalidOperationException("The server didn't say what removing this person would affect.");

    public async Task<string?> RemoveAsync(Guid householdId, Guid personId, RemovePersonRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(PeopleRoutes.Removal(householdId, personId)), request);
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return problem?.Errors.Values.SelectMany(v => v).FirstOrDefault() ?? problem?.Title ?? "This person couldn't be removed.";
        }

        response.EnsureSuccessStatusCode();
        return null;
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
        public string? Title { get; set; }

        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
