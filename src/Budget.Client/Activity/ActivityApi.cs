using System.Net.Http.Json;
using Budget.Shared.Activity;

namespace Budget.Client.Activity;

public interface IActivityApi
{
    /// <summary>Recent activity, newest first (BUD-139).</summary>
    Task<ActivityResponse> GetAsync(
        Guid householdId, int take, int? days = null, DateTimeOffset? before = null, Guid? who = null, IReadOnlyCollection<ActivityAreaDto>? areas = null);
}

public sealed class ActivityApi(HttpClient http) : IActivityApi
{
    public async Task<ActivityResponse> GetAsync(
        Guid householdId, int take, int? days = null, DateTimeOffset? before = null, Guid? who = null, IReadOnlyCollection<ActivityAreaDto>? areas = null) =>
        (await http.GetFromJsonAsync<ActivityResponse>(new Uri(ActivityRoutes.Activity(householdId, take, days, before, who, areas), UriKind.Relative)))!;
}
