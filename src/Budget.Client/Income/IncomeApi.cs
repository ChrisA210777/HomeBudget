using System.Net;
using System.Net.Http.Json;
using Budget.Shared.Households;
using Budget.Shared.Income;

namespace Budget.Client.Income;

/// <summary>The outcome of saving: the saved details, or why it wasn't saved.</summary>
public sealed record SaveResult(
    EmploymentIncomeDetails? Saved,
    IReadOnlyDictionary<string, string[]> Errors,
    bool Conflict)
{
    public bool Succeeded => Saved is not null;

    public static SaveResult Success(EmploymentIncomeDetails saved) => new(saved, new Dictionary<string, string[]>(), Conflict: false);
}

public interface IIncomeApi
{
    Task<IncomeStreamListResponse> ListAsync(Guid householdId, bool includeInactive);

    /// <returns>The details, or <c>null</c> if the stream doesn't exist (any more).</returns>
    Task<EmploymentIncomeDetails?> GetAsync(Guid householdId, Guid incomeStreamId);

    Task<SaveResult> CreateAsync(Guid householdId, SaveEmploymentIncomeRequest request);

    Task<SaveResult> UpdateAsync(Guid householdId, Guid incomeStreamId, SaveEmploymentIncomeRequest request);

    Task SetActiveAsync(Guid householdId, Guid incomeStreamId, bool active);

    Task DeleteAsync(Guid householdId, Guid incomeStreamId);

    Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId);
}

public sealed class IncomeApi(HttpClient http) : IIncomeApi
{
    public async Task<IncomeStreamListResponse> ListAsync(Guid householdId, bool includeInactive)
    {
        var url = IncomeRoutes.IncomeStreams(householdId) + (includeInactive ? "?includeInactive=true" : string.Empty);
        return (await http.GetFromJsonAsync<IncomeStreamListResponse>(Relative(url)))!;
    }

    public async Task<EmploymentIncomeDetails?> GetAsync(Guid householdId, Guid incomeStreamId)
    {
        using var response = await http.GetAsync(Relative(IncomeRoutes.IncomeStream(householdId, incomeStreamId)));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmploymentIncomeDetails>();
    }

    public async Task<SaveResult> CreateAsync(Guid householdId, SaveEmploymentIncomeRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(IncomeRoutes.IncomeStreams(householdId)), request);
        return await ToSaveResultAsync(response);
    }

    public async Task<SaveResult> UpdateAsync(Guid householdId, Guid incomeStreamId, SaveEmploymentIncomeRequest request)
    {
        using var response = await http.PutAsJsonAsync(Relative(IncomeRoutes.IncomeStream(householdId, incomeStreamId)), request);
        return await ToSaveResultAsync(response);
    }

    public async Task SetActiveAsync(Guid householdId, Guid incomeStreamId, bool active)
    {
        var url = active ? IncomeRoutes.Reactivate(householdId, incomeStreamId) : IncomeRoutes.Deactivate(householdId, incomeStreamId);
        using var response = await http.PostAsync(Relative(url), content: null);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid householdId, Guid incomeStreamId)
    {
        using var response = await http.DeleteAsync(Relative(IncomeRoutes.IncomeStream(householdId, incomeStreamId)));
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<HouseholdMemberSummary>>(Relative(MemberRoutes.Members(householdId))) ?? [];

    private static async Task<SaveResult> ToSaveResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return SaveResult.Success((await response.Content.ReadFromJsonAsync<EmploymentIncomeDetails>())!);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new SaveResult(null, new Dictionary<string, string[]>(), Conflict: true);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return new SaveResult(null, problem?.Errors ?? new Dictionary<string, string[]>(), Conflict: false);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    /// <summary>The parts of an RFC 7807 validation problem the client needs.</summary>
    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
