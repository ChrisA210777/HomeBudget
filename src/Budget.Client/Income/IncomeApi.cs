using System.Net;
using System.Net.Http.Json;
using Budget.Shared.Income;

namespace Budget.Client.Income;

/// <summary>The outcome of a change: the result, or field errors, or a conflict with someone else's change.</summary>
public sealed record ApiResult<T>(T? Value, IReadOnlyDictionary<string, string[]> Errors, bool Conflict)
    where T : class
{
    public bool Succeeded => Value is not null;

    public static ApiResult<T> Success(T value) => new(value, new Dictionary<string, string[]>(), Conflict: false);

    public static ApiResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors, Conflict: false);
}

public interface IIncomeApi
{
    Task<IncomeMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1);

    /// <returns>The income, or <c>null</c> if it doesn't exist (any more).</returns>
    Task<IncomeStreamDetails?> GetAsync(Guid householdId, Guid incomeStreamId);

    Task<ApiResult<IncomeStreamDetails>> CreateAsync(Guid householdId, CreateIncomeStreamRequest request);

    Task<ApiResult<IncomeStreamDetails>> UpdateAsync(Guid householdId, Guid incomeStreamId, UpdateIncomeStreamRequest request);

    Task DeleteAsync(Guid householdId, Guid incomeStreamId);

    Task<ApiResult<IncomeStreamDetails>> AddAmountChangeAsync(Guid householdId, Guid incomeStreamId, SaveAmountChangeRequest request);

    Task<ApiResult<IncomeStreamDetails>> CorrectAmountChangeAsync(Guid householdId, Guid incomeStreamId, Guid entryId, SaveAmountChangeRequest request);

    Task<ApiResult<IncomeStreamDetails>> DeleteAmountChangeAsync(Guid householdId, Guid incomeStreamId, Guid entryId, DeleteEntryRequest request);

    Task<ApiResult<IncomeStreamDetails>> AddStatusPeriodAsync(Guid householdId, Guid incomeStreamId, SaveStatusPeriodRequest request);

    Task<ApiResult<IncomeStreamDetails>> CorrectStatusPeriodAsync(Guid householdId, Guid incomeStreamId, Guid entryId, SaveStatusPeriodRequest request);

    Task<ApiResult<IncomeStreamDetails>> DeleteStatusPeriodAsync(Guid householdId, Guid incomeStreamId, Guid entryId, DeleteEntryRequest request);
}

public sealed class IncomeApi(HttpClient http) : IIncomeApi
{
    public async Task<IncomeMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1) =>
        (await http.GetFromJsonAsync<IncomeMonthResponse>(Relative(IncomeRoutes.ForMonth(householdId, year, month, all, months))))!;

    public async Task<IncomeStreamDetails?> GetAsync(Guid householdId, Guid incomeStreamId)
    {
        using var response = await http.GetAsync(Relative(IncomeRoutes.IncomeStream(householdId, incomeStreamId)));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IncomeStreamDetails>();
    }

    public Task<ApiResult<IncomeStreamDetails>> CreateAsync(Guid householdId, CreateIncomeStreamRequest request) =>
        SendAsync(HttpMethod.Post, IncomeRoutes.IncomeStreams(householdId), request);

    public Task<ApiResult<IncomeStreamDetails>> UpdateAsync(Guid householdId, Guid incomeStreamId, UpdateIncomeStreamRequest request) =>
        SendAsync(HttpMethod.Put, IncomeRoutes.IncomeStream(householdId, incomeStreamId), request);

    public async Task DeleteAsync(Guid householdId, Guid incomeStreamId)
    {
        using var response = await http.DeleteAsync(Relative(IncomeRoutes.IncomeStream(householdId, incomeStreamId)));
        response.EnsureSuccessStatusCode();
    }

    public Task<ApiResult<IncomeStreamDetails>> AddAmountChangeAsync(Guid householdId, Guid incomeStreamId, SaveAmountChangeRequest request) =>
        SendAsync(HttpMethod.Post, IncomeRoutes.AmountChanges(householdId, incomeStreamId), request);

    public Task<ApiResult<IncomeStreamDetails>> CorrectAmountChangeAsync(Guid householdId, Guid incomeStreamId, Guid entryId, SaveAmountChangeRequest request) =>
        SendAsync(HttpMethod.Put, IncomeRoutes.AmountChange(householdId, incomeStreamId, entryId), request);

    public Task<ApiResult<IncomeStreamDetails>> DeleteAmountChangeAsync(Guid householdId, Guid incomeStreamId, Guid entryId, DeleteEntryRequest request) =>
        SendAsync(HttpMethod.Delete, IncomeRoutes.AmountChange(householdId, incomeStreamId, entryId), request);

    public Task<ApiResult<IncomeStreamDetails>> AddStatusPeriodAsync(Guid householdId, Guid incomeStreamId, SaveStatusPeriodRequest request) =>
        SendAsync(HttpMethod.Post, IncomeRoutes.StatusPeriods(householdId, incomeStreamId), request);

    public Task<ApiResult<IncomeStreamDetails>> CorrectStatusPeriodAsync(Guid householdId, Guid incomeStreamId, Guid entryId, SaveStatusPeriodRequest request) =>
        SendAsync(HttpMethod.Put, IncomeRoutes.StatusPeriod(householdId, incomeStreamId, entryId), request);

    public Task<ApiResult<IncomeStreamDetails>> DeleteStatusPeriodAsync(Guid householdId, Guid incomeStreamId, Guid entryId, DeleteEntryRequest request) =>
        SendAsync(HttpMethod.Delete, IncomeRoutes.StatusPeriod(householdId, incomeStreamId, entryId), request);

    private async Task<ApiResult<IncomeStreamDetails>> SendAsync<TRequest>(HttpMethod method, string url, TRequest body)
    {
        using var request = new HttpRequestMessage(method, Relative(url)) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return ApiResult<IncomeStreamDetails>.Success((await response.Content.ReadFromJsonAsync<IncomeStreamDetails>())!);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new ApiResult<IncomeStreamDetails>(null, new Dictionary<string, string[]>(), Conflict: true);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return ApiResult<IncomeStreamDetails>.Invalid(problem?.Errors ?? []);
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

/// <summary>Date formatting used across the income pages, e.g. "16 Nov 2026".</summary>
public static class IncomeDates
{
    public static string Format(DateOnly date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    public static string MonthName(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    public static DateOnly? ToDateOnly(DateTime? value) => value is { } date ? DateOnly.FromDateTime(date) : null;

    public static DateTime? ToDateTime(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);
}
