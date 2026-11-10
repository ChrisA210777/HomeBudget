using System.Net.Http.Json;
using Budget.Shared.Outgoings;

namespace Budget.Client.Outgoings;

public interface IRenewalApi
{
    /// <summary>Outgoings coming up for renewal now, soonest first (BUD-52).</summary>
    Task<IReadOnlyList<RenewalComingUp>> ComingUpAsync(Guid householdId);

    /// <summary>Renewals over the next 12 months, soonest first (BUD-81).</summary>
    Task<RenewalsResponse> CalendarAsync(Guid householdId);
}

public sealed class RenewalApi(HttpClient http) : IRenewalApi
{
    public async Task<IReadOnlyList<RenewalComingUp>> ComingUpAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<RenewalComingUp>>(new Uri(OutgoingRoutes.ComingUp(householdId), UriKind.Relative)) ?? [];

    public async Task<RenewalsResponse> CalendarAsync(Guid householdId) =>
        (await http.GetFromJsonAsync<RenewalsResponse>(new Uri(OutgoingRoutes.Renewals(householdId), UriKind.Relative)))!;
}
