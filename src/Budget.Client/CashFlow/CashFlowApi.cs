using System.Net.Http.Json;
using Budget.Shared.CashFlow;

namespace Budget.Client.CashFlow;

/// <summary>Cash flow (BUD-92): each account's money in and out.</summary>
public interface ICashFlowApi
{
    Task<CashFlowMonthResponse> GetMonthAsync(Guid householdId, int year, int month);

    Task<CashFlowDaysResponse> GetDaysAsync(Guid householdId, DateOnly from, DateOnly to);
}

public sealed class CashFlowApi(HttpClient http) : ICashFlowApi
{
    public async Task<CashFlowMonthResponse> GetMonthAsync(Guid householdId, int year, int month) =>
        (await http.GetFromJsonAsync<CashFlowMonthResponse>(new Uri(CashFlowRoutes.Month(householdId, year, month), UriKind.Relative)))!;

    public async Task<CashFlowDaysResponse> GetDaysAsync(Guid householdId, DateOnly from, DateOnly to) =>
        (await http.GetFromJsonAsync<CashFlowDaysResponse>(new Uri(CashFlowRoutes.Days(householdId, from, to), UriKind.Relative)))!;
}
