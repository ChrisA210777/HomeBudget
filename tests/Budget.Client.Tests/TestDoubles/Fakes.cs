using Budget.Client.Account;
using Budget.Client.Income;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Budget.Shared.Income;

namespace Budget.Client.Tests.TestDoubles;

/// <summary>An account API whose signed-in user is a member of one household with the given role.</summary>
public sealed class FakeAccountApi(string role = "Admin", bool isOwner = true) : IAccountApi
{
    public static readonly Guid HouseholdId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public Task<IReadOnlyList<AuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuthProvider>>([]);

    public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<CurrentUserResponse?>(new CurrentUserResponse(
            UserId,
            "Chris Austin",
            "chris@outlook.com",
            [new HouseholdMembershipSummary(HouseholdId, "My Household", role, isOwner)],
            HouseholdId));

    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>An in-memory income API that records what the page asked it to do.</summary>
public sealed class FakeIncomeApi : IIncomeApi
{
    public IncomeStreamListResponse List { get; set; } = new([], 0m, 0m, "GBP");

    public EmploymentIncomeDetails? Details { get; set; }

    public IReadOnlyList<HouseholdMemberSummary> Members { get; set; } =
    [
        new(FakeAccountApi.UserId, "Chris Austin", "chris@outlook.com", "Admin", IsOwner: true, DateTimeOffset.UnixEpoch),
    ];

    /// <summary>What the next create or update returns; succeeds by default.</summary>
    public SaveResult? NextSaveResult { get; set; }

    public List<bool> ListCalls { get; } = [];

    public List<(Guid? Id, SaveEmploymentIncomeRequest Request)> Saves { get; } = [];

    public List<(Guid Id, bool Active)> ActiveChanges { get; } = [];

    public Task<IncomeStreamListResponse> ListAsync(Guid householdId, bool includeInactive)
    {
        ListCalls.Add(includeInactive);
        return Task.FromResult(List);
    }

    public Task<EmploymentIncomeDetails?> GetAsync(Guid householdId, Guid incomeStreamId) => Task.FromResult(Details);

    public Task<SaveResult> CreateAsync(Guid householdId, SaveEmploymentIncomeRequest request)
    {
        Saves.Add((null, request));
        return Task.FromResult(NextSaveResult ?? SaveResult.Success(ToDetails(Guid.NewGuid(), request)));
    }

    public Task<SaveResult> UpdateAsync(Guid householdId, Guid incomeStreamId, SaveEmploymentIncomeRequest request)
    {
        Saves.Add((incomeStreamId, request));
        return Task.FromResult(NextSaveResult ?? SaveResult.Success(ToDetails(incomeStreamId, request)));
    }

    public Task SetActiveAsync(Guid householdId, Guid incomeStreamId, bool active)
    {
        ActiveChanges.Add((incomeStreamId, active));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid householdId, Guid incomeStreamId) => Task.CompletedTask;

    public Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId) => Task.FromResult(Members);

    private static EmploymentIncomeDetails ToDetails(Guid id, SaveEmploymentIncomeRequest r) => new(
        id, r.Name, r.EmployerName, r.MemberUserId, r.PayFrequency, r.Kind, r.GrossAmount, r.AmountBasis,
        r.LowestGross, r.AverageGross, r.HighestGross, IsActive: true, Version: "AAAAAAAAAAE=");
}
