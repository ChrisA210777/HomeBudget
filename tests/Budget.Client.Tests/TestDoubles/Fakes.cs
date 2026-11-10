using Budget.Client.Account;
using Budget.Client.Budgeting;
using Budget.Client.Income;
using Budget.Client.Outgoings;
using Budget.Client.People;
using Budget.Shared.Account;
using Budget.Shared.Budgeting;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Budget.Shared.People;

namespace Budget.Client.Tests.TestDoubles;

/// <summary>An account API whose signed-in user is a member of one household with the given role.</summary>
public sealed class FakeAccountApi(string role = "Admin", bool isOwner = true, bool transfersEnabled = false) : IAccountApi
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
            [new HouseholdMembershipSummary(HouseholdId, "My Household", role, isOwner, TransfersEnabled: transfersEnabled)],
            HouseholdId));

    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>A clock fixed at a given UTC time, with UTC as the local time zone.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>A household with two people: Chris (you) and Jane.</summary>
public sealed class StaticPeopleApi : IPeopleApi
{
    public static readonly Guid ChrisId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid JaneId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid householdId) =>
        Task.FromResult<IReadOnlyList<PersonSummary>>(
        [
            new(ChrisId, "Chris Austin", FakeAccountApi.UserId, "Chris Austin", IsYou: true),
            new(JaneId, "Jane", null, null, IsYou: false),
        ]);

    public Task<SavePersonResult> AddAsync(Guid householdId, SavePersonRequest request) => throw new NotSupportedException();

    public Task<SavePersonResult> RenameAsync(Guid householdId, Guid personId, SavePersonRequest request) => throw new NotSupportedException();
}

/// <summary>An in-memory income API that records what the pages asked it to do.</summary>
public sealed class FakeIncomeApi : IIncomeApi
{
    public IncomeMonthResponse Month { get; set; } = new(2026, 11, [], 0m, "GBP");

    public IncomeStreamDetails? Details { get; set; }

    /// <summary>What the next change returns; by default it succeeds with <see cref="Details"/>.</summary>
    public ApiResult<IncomeStreamDetails>? NextResult { get; set; }

    public List<(int Year, int Month, bool All)> MonthCalls { get; } = [];

    public List<int> MonthsRequested { get; } = [];

    /// <summary>Each change as (operation, entry id, request).</summary>
    public List<(string Operation, Guid? EntryId, object Request)> Calls { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public int GetCalls { get; private set; }

    public Task<IncomeMonthResponse> GetMonthAsync(Guid householdId, int year, int month, bool all, int months = 1)
    {
        MonthCalls.Add((year, month, all));
        MonthsRequested.Add(months);
        return Task.FromResult(Month with { Year = year, Month = month, Months = months });
    }

    public Task<IncomeStreamDetails?> GetAsync(Guid householdId, Guid incomeStreamId)
    {
        GetCalls++;
        return Task.FromResult(Details);
    }

    public Task<ApiResult<IncomeStreamDetails>> CreateAsync(Guid householdId, CreateIncomeStreamRequest request) => Record("Create", null, request);

    public Task<ApiResult<IncomeStreamDetails>> UpdateAsync(Guid householdId, Guid incomeStreamId, UpdateIncomeStreamRequest request) => Record("Update", null, request);

    public Task DeleteAsync(Guid householdId, Guid incomeStreamId)
    {
        Deleted.Add(incomeStreamId);
        return Task.CompletedTask;
    }

    public Task<ApiResult<IncomeStreamDetails>> AddAmountChangeAsync(Guid householdId, Guid incomeStreamId, SaveAmountChangeRequest request) => Record("AddAmount", null, request);

    public Task<ApiResult<IncomeStreamDetails>> CorrectAmountChangeAsync(Guid householdId, Guid incomeStreamId, Guid entryId, SaveAmountChangeRequest request) => Record("CorrectAmount", entryId, request);

    public Task<ApiResult<IncomeStreamDetails>> DeleteAmountChangeAsync(Guid householdId, Guid incomeStreamId, Guid entryId, DeleteEntryRequest request) => Record("DeleteAmount", entryId, request);

    public Task<ApiResult<IncomeStreamDetails>> AddStatusPeriodAsync(Guid householdId, Guid incomeStreamId, SaveStatusPeriodRequest request) => Record("AddPeriod", null, request);

    public Task<ApiResult<IncomeStreamDetails>> CorrectStatusPeriodAsync(Guid householdId, Guid incomeStreamId, Guid entryId, SaveStatusPeriodRequest request) => Record("CorrectPeriod", entryId, request);

    public Task<ApiResult<IncomeStreamDetails>> DeleteStatusPeriodAsync(Guid householdId, Guid incomeStreamId, Guid entryId, DeleteEntryRequest request) => Record("DeletePeriod", entryId, request);

    private Task<ApiResult<IncomeStreamDetails>> Record(string operation, Guid? entryId, object request)
    {
        Calls.Add((operation, entryId, request));
        var result = NextResult ?? ApiResult<IncomeStreamDetails>.Success(Details ?? IncomeSamples.Salary());
        NextResult = null;
        return Task.FromResult(result);
    }
}

/// <summary>Sample incomes for the page tests.</summary>
public static class IncomeSamples
{
    public static readonly Guid SalaryId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid FirstChangeId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid RiseId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid LeaveId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public static IncomeStreamDetails Salary() => new(
        SalaryId,
        "Chris – salary",
        StaticPeopleApi.ChrisId,
        "Chris Austin",
        AmountToday: 3000m,
        ReceivedAmount: 3000m,
        Every: 1,
        Unit: PaymentUnitDto.Month,
        StartsOn: new DateOnly(2026, 1, 1),
        EndsOn: null,
        AmountChanges:
        [
            new(FirstChangeId, new DateOnly(2026, 1, 1), 3000m, 1, PaymentUnitDto.Month, 3000m, "Starting amount", "Chris Austin", DateTimeOffset.UnixEpoch, IsUpcoming: false, IsFirst: true),
            new(RiseId, new DateOnly(2026, 12, 1), 3300m, 1, PaymentUnitDto.Month, 3300m, "10% pay rise", "Chris Austin", DateTimeOffset.UnixEpoch, IsUpcoming: true, IsFirst: false),
        ],
        StatusPeriods:
        [
            new(LeaveId, IncomeStatusReasonDto.SickLeave, new DateOnly(2026, 11, 16), null, 470m, "Statutory sick pay", "Chris Austin", DateTimeOffset.UnixEpoch, IsUpcoming: false),
        ],
        Corrections: [],
        CurrencyCode: "GBP",
        Version: "AAAAAAAAAAE=");
}

/// <summary>A budget for any month, settable per test.</summary>
public sealed class FakeBudgetApi : IBudgetApi
{
    public BudgetMonthResponse? Month { get; set; }

    public List<(int? Year, int? Month)> Requests { get; } = [];

    public Task<BudgetMonthResponse> GetMonthAsync(Guid householdId, int? year = null, int? month = null)
    {
        Requests.Add((year, month));
        var budget = Month ?? Sample(year ?? 2026, month ?? 11, income: 3000m, essential: 1090m, preferred: 520m, optional: 100m);
        return Task.FromResult(budget with { Year = year ?? budget.Year, Month = month ?? budget.Month });
    }

    public static BudgetMonthResponse Sample(int year, int month, decimal income, decimal essential, decimal preferred, decimal optional) => new(
        year,
        month,
        "GBP",
        income,
        essential,
        preferred,
        optional,
        income - essential,
        income - essential - preferred,
        income - essential - preferred - optional,
        [
            new(OutgoingKindDto.Bill, essential),
            new(OutgoingKindDto.Savings, optional),
            new(OutgoingKindDto.Allowance, preferred),
        ],
        [
            new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"), "Housing", essential),
            new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"), "Food & household", preferred),
            new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000003"), "Savings & set-asides", optional),
        ]);
}

/// <summary>Renewals coming up, settable per test (none by default).</summary>
public sealed class FakeRenewalApi : IRenewalApi
{
    public IReadOnlyList<RenewalComingUp> ComingUp { get; set; } = [];

    public Task<IReadOnlyList<RenewalComingUp>> ComingUpAsync(Guid householdId) => Task.FromResult(ComingUp);

    public RenewalsResponse Calendar { get; set; } = new(new DateOnly(2026, 11, 5), new DateOnly(2027, 11, 4), "GBP", []);

    public Task<RenewalsResponse> CalendarAsync(Guid householdId) => Task.FromResult(Calendar);
}

/// <summary>Records household switches and creations; the rest isn't used by the tests that need this.</summary>
public sealed class RecordingHouseholdApi : Budget.Client.Households.IHouseholdApi
{
    public List<Guid> SwitchedTo { get; } = [];

    public List<Budget.Shared.Households.CreateHouseholdRequest> Created { get; } = [];

    public Dictionary<string, string[]>? CreateErrors { get; set; }

    public Task<Budget.Client.Households.CreateHouseholdResult> CreateAsync(Budget.Shared.Households.CreateHouseholdRequest request)
    {
        Created.Add(request);
        return Task.FromResult(CreateErrors is { } errors
            ? new Budget.Client.Households.CreateHouseholdResult(null, errors)
            : new Budget.Client.Households.CreateHouseholdResult(
                new Budget.Shared.Households.HouseholdResponse(Guid.NewGuid(), request.Name.Trim(), "GBP", "Admin", true, 1),
                new Dictionary<string, string[]>()));
    }

    public Task<Budget.Shared.Households.HouseholdSetupResponse> GetSetupAsync(Guid householdId) => throw new NotSupportedException();

    public Task<bool> SwitchToAsync(Guid householdId)
    {
        SwitchedTo.Add(householdId);
        return Task.FromResult(true);
    }

    public Task<Budget.Client.Households.RenameHouseholdResult> RenameAsync(Guid householdId, Budget.Shared.Households.RenameHouseholdRequest request) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Budget.Shared.Households.HouseholdMemberSummary>> GetMembersAsync(Guid householdId) =>
        throw new NotSupportedException();

    public Task<string?> ChangeRoleAsync(Guid householdId, Guid userId, string role) => throw new NotSupportedException();

    public Task<string?> RemoveMemberAsync(Guid householdId, Guid userId) => throw new NotSupportedException();

    public Task<string?> LeaveAsync(Guid householdId) => throw new NotSupportedException();

    public Task DeleteAsync(Guid householdId) => throw new NotSupportedException();
}

/// <summary>Settings saved in memory, recording each change (BUD-87).</summary>
public sealed class FakeUserSettingsApi : IUserSettingsApi
{
    public UserSettings Saved { get; set; } = UserSettings.Defaults;

    public List<UpdateUserSettingsRequest> Updates { get; } = [];

    /// <summary>Behave as if the server can't be reached.</summary>
    public bool Offline { get; set; }

    public Task<UserSettings> GetAsync() =>
        Offline ? Task.FromException<UserSettings>(new HttpRequestException("Offline")) : Task.FromResult(Saved);

    public Task<UserSettings> UpdateAsync(UpdateUserSettingsRequest request)
    {
        Updates.Add(request);
        if (Offline)
        {
            return Task.FromException<UserSettings>(new HttpRequestException("Offline"));
        }

        Saved = Saved with
        {
            FirstDayOfWeek = request.FirstDayOfWeek ?? Saved.FirstDayOfWeek,
            OutgoingsPeriod = request.OutgoingsPeriod ?? Saved.OutgoingsPeriod,
            OutgoingsGrouping = request.OutgoingsGrouping ?? Saved.OutgoingsGrouping,
            IncomePeriod = request.IncomePeriod ?? Saved.IncomePeriod,
        };
        return Task.FromResult(Saved);
    }
}

/// <summary>A household with two accounts and a pot (BUD-89), for pages that offer accounts to choose from.</summary>
public sealed class StaticAccountsApi : Budget.Client.Accounts.IAccountsApi
{
    public Task<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>> ListAsync(Guid householdId)
    {
        var monzo = Guid.NewGuid();
        return Task.FromResult<IReadOnlyList<Budget.Shared.Accounts.AccountSummary>>(
        [
            new(Guid.NewGuid(), "Chris current", null, null, null, null, false, "Chris current", 0),
            new(monzo, "Joint Monzo", "Monzo", null, null, null, false, "Joint Monzo", 0),
            new(Guid.NewGuid(), "Bills", null, null, null, monzo, false, "Joint Monzo › Bills", 0),
        ]);
    }

    public Task<Budget.Client.Accounts.AccountsResult> AddAsync(Guid householdId, Budget.Shared.Accounts.SaveAccountRequest request) => throw new NotSupportedException();

    public Task<Budget.Client.Accounts.AccountsResult> UpdateAsync(Guid householdId, Guid accountId, Budget.Shared.Accounts.SaveAccountRequest request) =>
        throw new NotSupportedException();

    public Task<Budget.Client.Accounts.AccountsResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived) => throw new NotSupportedException();

    public Task<Budget.Client.Accounts.AccountsResult> MergeAsync(Guid householdId, Guid accountId, Guid intoAccountId) => throw new NotSupportedException();
}

/// <summary>Records the household settings saved (BUD-91); nothing else is used by the pages under test.</summary>
public sealed class SettingsHouseholdApi : Budget.Client.Households.IHouseholdApi
{
    public List<Budget.Shared.Households.HouseholdSettingsRequest> Saved { get; } = [];

    public Task<Budget.Shared.Households.HouseholdResponse> SaveSettingsAsync(Guid householdId, Budget.Shared.Households.HouseholdSettingsRequest request)
    {
        Saved.Add(request);
        return Task.FromResult(new Budget.Shared.Households.HouseholdResponse(householdId, "My Household", "GBP", "Admin", true, 1, request.TransfersEnabled));
    }

    public Task<Budget.Client.Households.CreateHouseholdResult> CreateAsync(Budget.Shared.Households.CreateHouseholdRequest request) => throw new NotSupportedException();

    public Task<bool> SwitchToAsync(Guid householdId) => throw new NotSupportedException();

    public Task<Budget.Client.Households.RenameHouseholdResult> RenameAsync(Guid householdId, Budget.Shared.Households.RenameHouseholdRequest request) => throw new NotSupportedException();

    public Task<IReadOnlyList<Budget.Shared.Households.HouseholdMemberSummary>> GetMembersAsync(Guid householdId) => throw new NotSupportedException();

    public Task<Budget.Shared.Households.HouseholdSetupResponse> GetSetupAsync(Guid householdId) => throw new NotSupportedException();

    public Task<string?> ChangeRoleAsync(Guid householdId, Guid userId, string role) => throw new NotSupportedException();

    public Task<string?> RemoveMemberAsync(Guid householdId, Guid userId) => throw new NotSupportedException();

    public Task DeleteAsync(Guid householdId) => throw new NotSupportedException();

    public Task<string?> LeaveAsync(Guid householdId) => throw new NotSupportedException();
}
