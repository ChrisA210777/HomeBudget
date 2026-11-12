using System.Net.Http.Json;
using Budget.Shared.Accounts;
using Budget.Shared.CashFlow;

namespace Budget.Client.CashFlow;

/// <summary>Cash flow (BUD-92): each account's money in and out.</summary>
public interface ICashFlowApi
{
    Task<CashFlowMonthResponse> GetMonthAsync(Guid householdId, int year, int month);

    /// <param name="accountMonths">From and to are a calendar month, and each account shows its own month for it (BUD-138).</param>
    Task<CashFlowDaysResponse> GetDaysAsync(Guid householdId, DateOnly from, DateOnly to, bool accountMonths);

    /// <summary>Starts an account's month when a payment arrives, from Cash flow's suggestion (BUD-136). False if refused.</summary>
    Task<bool> SetMonthStartAsync(Guid householdId, Guid accountId, Guid? paymentId);
}

public sealed class CashFlowApi(HttpClient http) : ICashFlowApi
{
    public async Task<CashFlowMonthResponse> GetMonthAsync(Guid householdId, int year, int month) =>
        (await http.GetFromJsonAsync<CashFlowMonthResponse>(new Uri(CashFlowRoutes.Month(householdId, year, month), UriKind.Relative)))!;

    public async Task<CashFlowDaysResponse> GetDaysAsync(Guid householdId, DateOnly from, DateOnly to, bool accountMonths) =>
        (await http.GetFromJsonAsync<CashFlowDaysResponse>(new Uri(CashFlowRoutes.Days(householdId, from, to, accountMonths), UriKind.Relative)))!;

    public async Task<bool> SetMonthStartAsync(Guid householdId, Guid accountId, Guid? paymentId)
    {
        using var response = await http.PutAsJsonAsync(
            new Uri(HouseholdAccountRoutes.MonthStart(householdId, accountId), UriKind.Relative),
            new SetMonthStartRequest { MonthStartsWithId = paymentId });
        return response.IsSuccessStatusCode;
    }
}
