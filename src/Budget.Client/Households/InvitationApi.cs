using System.Net;
using System.Net.Http.Json;
using Budget.Client.Income;
using Budget.Shared.Households;

namespace Budget.Client.Households;

/// <summary>Invitations to join a household (ADR-015).</summary>
public interface IInvitationApi
{
    Task<IReadOnlyList<InvitationSummary>> ListAsync(Guid householdId);

    Task<ApiResult<InvitationSummary>> CreateAsync(Guid householdId, CreateInvitationRequest request);

    Task RevokeAsync(Guid householdId, Guid invitationId);

    /// <returns>What the link is for, or <c>null</c> if there's no such invitation.</returns>
    Task<InvitationPreview?> PreviewAsync(string token);

    /// <returns>The household joined, or the reason it couldn't be.</returns>
    Task<(AcceptInvitationResponse? Joined, string? Problem)> AcceptAsync(string token);
}

public sealed class InvitationApi(HttpClient http) : IInvitationApi
{
    public async Task<IReadOnlyList<InvitationSummary>> ListAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<InvitationSummary>>(Relative(InvitationRoutes.Invitations(householdId))) ?? [];

    public async Task<ApiResult<InvitationSummary>> CreateAsync(Guid householdId, CreateInvitationRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(InvitationRoutes.Invitations(householdId)), request);
        if (response.IsSuccessStatusCode)
        {
            return ApiResult<InvitationSummary>.Success((await response.Content.ReadFromJsonAsync<InvitationSummary>())!);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>();
            return ApiResult<InvitationSummary>.Invalid(problem?.Errors ?? []);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unexpected response.");
    }

    public async Task RevokeAsync(Guid householdId, Guid invitationId)
    {
        using var response = await http.DeleteAsync(Relative(InvitationRoutes.Invitation(householdId, invitationId)));
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<InvitationPreview?> PreviewAsync(string token)
    {
        using var response = await http.GetAsync(Relative(InvitationRoutes.ByToken(token)));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InvitationPreview>();
    }

    public async Task<(AcceptInvitationResponse? Joined, string? Problem)> AcceptAsync(string token)
    {
        using var response = await http.PostAsync(Relative(InvitationRoutes.Accept(token)), null);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<AcceptInvitationResponse>(), null);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            var problem = response.StatusCode == HttpStatusCode.NotFound ? null : await response.Content.ReadFromJsonAsync<Problem>();
            return (null, problem?.Detail ?? "This invitation link isn't valid.");
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unexpected response.");
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    private sealed class Problem
    {
        public string? Detail { get; set; }

        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
