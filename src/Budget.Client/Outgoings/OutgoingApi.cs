using System.Net;
using System.Net.Http.Json;
using Budget.Client.Income;
using Budget.Shared.Outgoings;

namespace Budget.Client.Outgoings;

public interface IOutgoingApi
{
    Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request);

    /// <summary>Account names already used for "paid from", to suggest as people type.</summary>
    Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId);
}

public sealed class OutgoingApi(HttpClient http) : IOutgoingApi
{
    public async Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(OutgoingRoutes.Outgoings(householdId)), request);
        if (response.IsSuccessStatusCode)
        {
            return ApiResult<OutgoingCreatedResponse>.Success((await response.Content.ReadFromJsonAsync<OutgoingCreatedResponse>())!);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return ApiResult<OutgoingCreatedResponse>.Invalid(problem?.Errors ?? []);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    public async Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<string>>(Relative(OutgoingRoutes.PaidFromNames(householdId))) ?? [];

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
