using System.Net.Http.Json;
using Budget.Shared.Budgeting;

namespace Budget.Client.Budgeting;

public interface IBudgetApi
{
    /// <summary>The budget for a month; this month when <paramref name="year"/> and <paramref name="month"/> are null.</summary>
    Task<BudgetMonthResponse> GetMonthAsync(Guid householdId, int? year = null, int? month = null);
}

public sealed class BudgetApi(HttpClient http) : IBudgetApi
{
    public async Task<BudgetMonthResponse> GetMonthAsync(Guid householdId, int? year = null, int? month = null) =>
        (await http.GetFromJsonAsync<BudgetMonthResponse>(new Uri(BudgetRoutes.Month(householdId, year, month), UriKind.Relative)))!;
}
