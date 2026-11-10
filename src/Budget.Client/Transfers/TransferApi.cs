using System.Net;
using System.Net.Http.Json;
using Budget.Client.Income;
using Budget.Client.Outgoings;
using Budget.Shared.Outgoings;
using Budget.Shared.Transfers;

namespace Budget.Client.Transfers;

/// <summary>Transfers between accounts (BUD-91).</summary>
public interface ITransferApi
{
    Task<TransfersResponse> ListAsync(Guid householdId);

    /// <returns>The transfer, or <c>null</c> if it doesn't exist (any more).</returns>
    Task<TransferDetailsResponse?> GetAsync(Guid householdId, Guid transferId);

    Task<ApiResult<TransferDetailsResponse>> CreateAsync(Guid householdId, CreateTransferRequest request);

    Task<ApiResult<TransferDetailsResponse>> UpdateAsync(Guid householdId, Guid transferId, UpdateTransferRequest request);

    Task DeleteAsync(Guid householdId, Guid transferId);

    Task<ApiResult<TransferDetailsResponse>> SendAsync(HttpMethod method, string url, object body);
}

public sealed class TransferApi(HttpClient http) : ITransferApi
{
    public async Task<TransfersResponse> ListAsync(Guid householdId) =>
        (await http.GetFromJsonAsync<TransfersResponse>(Relative(TransferRoutes.Transfers(householdId))))!;

    public async Task<TransferDetailsResponse?> GetAsync(Guid householdId, Guid transferId)
    {
        using var response = await http.GetAsync(Relative(TransferRoutes.Transfer(householdId, transferId)));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TransferDetailsResponse>();
    }

    public Task<ApiResult<TransferDetailsResponse>> CreateAsync(Guid householdId, CreateTransferRequest request) =>
        SendAsync(HttpMethod.Post, TransferRoutes.Transfers(householdId), request);

    public Task<ApiResult<TransferDetailsResponse>> UpdateAsync(Guid householdId, Guid transferId, UpdateTransferRequest request) =>
        SendAsync(HttpMethod.Put, TransferRoutes.Transfer(householdId, transferId), request);

    public async Task DeleteAsync(Guid householdId, Guid transferId)
    {
        using var response = await http.DeleteAsync(Relative(TransferRoutes.Transfer(householdId, transferId)));
        response.EnsureSuccessStatusCode();
    }

    public async Task<ApiResult<TransferDetailsResponse>> SendAsync(HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, Relative(url)) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return ApiResult<TransferDetailsResponse>.Success((await response.Content.ReadFromJsonAsync<TransferDetailsResponse>())!);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new ApiResult<TransferDetailsResponse>(null, new Dictionary<string, string[]>(), Conflict: true);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return ApiResult<TransferDetailsResponse>.Invalid(problem?.Errors ?? []);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unexpected response.");
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}

/// <summary>A transfer's history, saved through <see cref="ITransferApi"/>.</summary>
public sealed class TransferScheduleBackend(ITransferApi api, Guid householdId, Guid transferId) : IScheduleBackend<TransferDetailsResponse>
{
    /// <summary>Its name, or "From → To" when it hasn't got one.</summary>
    public static string Title(string? name, string from, string to) => string.IsNullOrWhiteSpace(name) ? $"{from} → {to}" : name;

    public static ScheduleView View(TransferDetailsResponse d) => new(
        Title(d.Name, d.FromName, d.ToName), d.Every, d.Unit, d.NextPayment, d.EndsOn, d.UpcomingPayments, d.AmountChanges, d.StatusPeriods, d.Corrections, d.CurrencyCode, d.Version);

    public Task<TransferDetailsResponse?> GetAsync() => api.GetAsync(householdId, transferId);

    public Task DeleteAsync() => api.DeleteAsync(householdId, transferId);

    public Task<ApiResult<TransferDetailsResponse>> AddAmountChangeAsync(SaveOutgoingAmountRequest request) =>
        api.SendAsync(HttpMethod.Post, TransferRoutes.AmountChanges(householdId, transferId), request);

    public Task<ApiResult<TransferDetailsResponse>> CorrectAmountChangeAsync(Guid entryId, SaveOutgoingAmountRequest request) =>
        api.SendAsync(HttpMethod.Put, TransferRoutes.AmountChange(householdId, transferId, entryId), request);

    public Task<ApiResult<TransferDetailsResponse>> DeleteAmountChangeAsync(Guid entryId, DeleteOutgoingEntryRequest request) =>
        api.SendAsync(HttpMethod.Delete, TransferRoutes.AmountChange(householdId, transferId, entryId), request);

    public Task<ApiResult<TransferDetailsResponse>> AddStatusPeriodAsync(SaveOutgoingStatusRequest request) =>
        api.SendAsync(HttpMethod.Post, TransferRoutes.StatusPeriods(householdId, transferId), request);

    public Task<ApiResult<TransferDetailsResponse>> CorrectStatusPeriodAsync(Guid entryId, SaveOutgoingStatusRequest request) =>
        api.SendAsync(HttpMethod.Put, TransferRoutes.StatusPeriod(householdId, transferId, entryId), request);

    public Task<ApiResult<TransferDetailsResponse>> DeleteStatusPeriodAsync(Guid entryId, DeleteOutgoingEntryRequest request) =>
        api.SendAsync(HttpMethod.Delete, TransferRoutes.StatusPeriod(householdId, transferId, entryId), request);
}
