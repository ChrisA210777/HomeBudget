using System.Net;
using System.Net.Http.Json;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Households;

namespace Budget.Client.Households;

/// <summary>
/// The outcome of a rename: the renamed household, field errors, or <see cref="CurrentName"/> when
/// someone else renamed it first.
/// </summary>
public sealed record RenameHouseholdResult(HouseholdResponse? Household, IReadOnlyDictionary<string, string[]> Errors, string? CurrentName = null)
{
    public bool Succeeded => Household is not null;

    public bool Conflict => CurrentName is not null;
}

/// <summary>The outcome of creating a household: the new household, or field errors.</summary>
public sealed record CreateHouseholdResult(HouseholdResponse? Household, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Household is not null;
}

public interface IHouseholdApi
{
    /// <summary>Creates a household owned by the signed-in user. It becomes their active household.</summary>
    Task<CreateHouseholdResult> CreateAsync(CreateHouseholdRequest request);

    /// <summary>Switches the signed-in user to another of their households.</summary>
    /// <returns>False if they're no longer a member of it.</returns>
    Task<bool> SwitchToAsync(Guid householdId);

    Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request);

    /// <returns>The household's members, owner first, then by name.</returns>
    Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId);

    /// <returns>How many incomes, outgoings and people the household has.</returns>
    Task<HouseholdSetupResponse> GetSetupAsync(Guid householdId);

    /// <returns><c>null</c> on success, otherwise why it was refused.</returns>
    Task<string?> ChangeRoleAsync(Guid householdId, Guid userId, string role);

    /// <returns><c>null</c> on success, otherwise why it was refused.</returns>
    Task<string?> RemoveMemberAsync(Guid householdId, Guid userId);

    /// <summary>Deletes the household and everything in it (owner only).</summary>
    Task DeleteAsync(Guid householdId);

    /// <summary>The signed-in user leaves the household.</summary>
    /// <returns><c>null</c> on success, otherwise why it was refused.</returns>
    Task<string?> LeaveAsync(Guid householdId);
}

public sealed class HouseholdApi(HttpClient http) : IHouseholdApi
{
    public async Task<CreateHouseholdResult> CreateAsync(CreateHouseholdRequest request)
    {
        using var response = await http.PostAsJsonAsync(new Uri(HouseholdRoutes.Households, UriKind.Relative), request);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return new CreateHouseholdResult(null, problem?.Errors ?? []);
        }

        response.EnsureSuccessStatusCode();
        return new CreateHouseholdResult(await response.Content.ReadFromJsonAsync<HouseholdResponse>(), new Dictionary<string, string[]>());
    }

    public async Task<bool> SwitchToAsync(Guid householdId)
    {
        using var response = await http.PutAsJsonAsync(new Uri(AccountRoutes.ActiveHousehold, UriKind.Relative), new SetActiveHouseholdRequest(householdId));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<HouseholdSetupResponse> GetSetupAsync(Guid householdId) =>
        await http.GetFromJsonAsync<HouseholdSetupResponse>(new Uri(HouseholdRoutes.Setup(householdId), UriKind.Relative))
        ?? new HouseholdSetupResponse(0, 0, 0);

    public async Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<HouseholdMemberSummary>>(new Uri(MemberRoutes.Members(householdId), UriKind.Relative)) ?? [];

    public async Task<string?> ChangeRoleAsync(Guid householdId, Guid userId, string role)
    {
        using var response = await http.PutAsJsonAsync(new Uri(MemberRoutes.Role(householdId, userId), UriKind.Relative), new ChangeRoleRequest { Role = role });
        return await RefusalAsync(response);
    }

    public async Task<string?> RemoveMemberAsync(Guid householdId, Guid userId)
    {
        using var response = await http.DeleteAsync(new Uri(MemberRoutes.Member(householdId, userId), UriKind.Relative));
        return await RefusalAsync(response);
    }

    public async Task DeleteAsync(Guid householdId)
    {
        using var response = await http.DeleteAsync(new Uri(HouseholdRoutes.Household(householdId), UriKind.Relative));
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> LeaveAsync(Guid householdId)
    {
        using var response = await http.DeleteAsync(new Uri(MemberRoutes.Membership(householdId), UriKind.Relative));
        return await RefusalAsync(response);
    }

    /// <summary>Turns a 400 into its message and a 404 into "no longer a member"; anything else unexpected throws.</summary>
    private static async Task<string?> RefusalAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
            return problem?.Errors?.Values.SelectMany(v => v).FirstOrDefault() ?? problem?.Title ?? "That change isn't allowed.";
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return "They're no longer a member of this household.";
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    public async Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request)
    {
        var url = new Uri(HouseholdRoutes.Household(householdId), UriKind.Relative);
        using var response = await http.PutAsJsonAsync(url, request);

        if (response.IsSuccessStatusCode)
        {
            return new RenameHouseholdResult(await response.Content.ReadFromJsonAsync<HouseholdResponse>(), new Dictionary<string, string[]>());
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
            return new RenameHouseholdResult(null, problem?.Errors ?? []);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var current = await http.GetFromJsonAsync<HouseholdResponse>(url);
            return new RenameHouseholdResult(null, new Dictionary<string, string[]>(), current!.Name);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    private sealed class ProblemResponse
    {
        public string? Title { get; set; }

        public Dictionary<string, string[]>? Errors { get; set; }
    }

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
