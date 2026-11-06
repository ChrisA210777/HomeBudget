using System.Net;
using System.Net.Http.Json;
using Budget.Client.Income;
using Budget.Shared.Outgoings;

namespace Budget.Client.Outgoings;

public interface IOutgoingApi
{
    Task<OutgoingMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all);

    Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request);

    /// <summary>Account names already used for "paid from", to suggest as people type.</summary>
    Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId);

    /// <returns>The outgoing, or <c>null</c> if it doesn't exist (any more).</returns>
    Task<OutgoingDetailsResponse?> GetAsync(Guid householdId, Guid outgoingId);

    Task<ApiResult<OutgoingDetailsResponse>> UpdateAsync(Guid householdId, Guid outgoingId, UpdateOutgoingRequest request);

    Task DeleteAsync(Guid householdId, Guid outgoingId);

    Task<ApiResult<OutgoingDetailsResponse>> AddAmountChangeAsync(Guid householdId, Guid outgoingId, SaveOutgoingAmountRequest request);

    Task<ApiResult<OutgoingDetailsResponse>> CorrectAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingAmountRequest request);

    Task<ApiResult<OutgoingDetailsResponse>> DeleteAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request);

    Task<ApiResult<OutgoingDetailsResponse>> AddStatusPeriodAsync(Guid householdId, Guid outgoingId, SaveOutgoingStatusRequest request);

    Task<ApiResult<OutgoingDetailsResponse>> CorrectStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingStatusRequest request);

    Task<ApiResult<OutgoingDetailsResponse>> DeleteStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request);
}

public sealed class OutgoingApi(HttpClient http) : IOutgoingApi
{
    public async Task<OutgoingMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all) =>
        (await http.GetFromJsonAsync<OutgoingMonthResponse>(Relative(OutgoingRoutes.ForMonth(householdId, year, month, all))))!;

    public Task<ApiResult<OutgoingCreatedResponse>> CreateAsync(Guid householdId, CreateOutgoingRequest request) =>
        SendAsync<OutgoingCreatedResponse>(HttpMethod.Post, OutgoingRoutes.Outgoings(householdId), request);

    public async Task<IReadOnlyList<string>> PaidFromNamesAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<string>>(Relative(OutgoingRoutes.PaidFromNames(householdId))) ?? [];

    public async Task<OutgoingDetailsResponse?> GetAsync(Guid householdId, Guid outgoingId)
    {
        using var response = await http.GetAsync(Relative(OutgoingRoutes.Outgoing(householdId, outgoingId)));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OutgoingDetailsResponse>();
    }

    public Task<ApiResult<OutgoingDetailsResponse>> UpdateAsync(Guid householdId, Guid outgoingId, UpdateOutgoingRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Put, OutgoingRoutes.Outgoing(householdId, outgoingId), request);

    public async Task DeleteAsync(Guid householdId, Guid outgoingId)
    {
        using var response = await http.DeleteAsync(Relative(OutgoingRoutes.Outgoing(householdId, outgoingId)));
        response.EnsureSuccessStatusCode();
    }

    public Task<ApiResult<OutgoingDetailsResponse>> AddAmountChangeAsync(Guid householdId, Guid outgoingId, SaveOutgoingAmountRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Post, OutgoingRoutes.AmountChanges(householdId, outgoingId), request);

    public Task<ApiResult<OutgoingDetailsResponse>> CorrectAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingAmountRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Put, OutgoingRoutes.AmountChange(householdId, outgoingId, entryId), request);

    public Task<ApiResult<OutgoingDetailsResponse>> DeleteAmountChangeAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Delete, OutgoingRoutes.AmountChange(householdId, outgoingId, entryId), request);

    public Task<ApiResult<OutgoingDetailsResponse>> AddStatusPeriodAsync(Guid householdId, Guid outgoingId, SaveOutgoingStatusRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Post, OutgoingRoutes.StatusPeriods(householdId, outgoingId), request);

    public Task<ApiResult<OutgoingDetailsResponse>> CorrectStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingStatusRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Put, OutgoingRoutes.StatusPeriod(householdId, outgoingId, entryId), request);

    public Task<ApiResult<OutgoingDetailsResponse>> DeleteStatusPeriodAsync(Guid householdId, Guid outgoingId, Guid entryId, DeleteOutgoingEntryRequest request) =>
        SendAsync<OutgoingDetailsResponse>(HttpMethod.Delete, OutgoingRoutes.StatusPeriod(householdId, outgoingId, entryId), request);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object body)
        where T : class
    {
        using var request = new HttpRequestMessage(method, Relative(url)) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return ApiResult<T>.Success((await response.Content.ReadFromJsonAsync<T>())!);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new ApiResult<T>(null, new Dictionary<string, string[]>(), Conflict: true);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return ApiResult<T>.Invalid(problem?.Errors ?? []);
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
