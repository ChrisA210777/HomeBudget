using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Domain.Transfers;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.CashFlow;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.CashFlow;

/// <summary>/api/households/{id}/cash-flow (BUD-92).</summary>
[Collection(ApiCollection.Name)]
public class CashFlowApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Each_account_shows_its_money_in_and_out_for_the_month_with_pots_rolled_up()
    {
        var (client, householdId) = await HouseholdAsync();

        var month = (await client.GetFromJsonAsync<CashFlowMonthResponse>(Url(CashFlowRoutes.Month(householdId, 2026, 11))))!;

        Assert.Equal(new[] { "Current", "Joint", "Not set" }, month.Accounts.Select(a => a.Name));
        var current = month.Accounts[0];
        Assert.Equal((3000m, 1000m, 2000m), (current.TotalIn, current.TotalOut, current.Net));
        Assert.Equal(new[] { (CashFlowKindDto.TransferOut, "Bills top-up", (string?)null) }, current.Out.Select(e => (e.Kind, e.Name, e.Pot)));

        var joint = month.Accounts[1];
        Assert.Equal((1000m, 1150m, -150m), (joint.TotalIn, joint.TotalOut, joint.Net));
        Assert.Equal(new[] { (CashFlowKindDto.TransferIn, "Bills top-up", (string?)"Bills") }, joint.In.Select(e => (e.Kind, e.Name, e.Pot)));
        Assert.Equal(new[] { ("Mortgage", (string?)"Bills"), ("Council tax", null) }, joint.Out.Select(e => (e.Name, e.Pot)));

        // A transfer between an account and its own pot stays in the account, so it isn't shown.
        Assert.DoesNotContain(joint.Out, e => e.Name == "Holiday saving");

        Assert.Equal((0m, 20m), (month.Accounts[2].TotalIn, month.Accounts[2].TotalOut));
    }

    [Fact]
    public async Task Day_by_day_shows_a_running_total_and_the_days_that_are_short()
    {
        var (client, householdId) = await HouseholdAsync();

        var days = (await client.GetFromJsonAsync<CashFlowDaysResponse>(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)))))!;

        var joint = days.Accounts.Single(a => a.Name == "Joint");
        // The mortgage on the 1st and council tax on the 5th go out before the top-up arrives on the 25th.
        Assert.Equal(
            new[] { (new DateOnly(2026, 11, 1), -1000m, true), (new DateOnly(2026, 11, 5), -1150m, true), (new DateOnly(2026, 11, 25), -150m, true) },
            joint.Days.Select(d => (d.Date, d.RunningTotal, d.IsShort)));
        var current = days.Accounts.Single(a => a.Name == "Current");
        Assert.Equal(new[] { (new DateOnly(2026, 11, 25), 2000m, false) }, current.Days.Select(d => (d.Date, d.RunningTotal, d.IsShort)));
        Assert.Equal(2, current.Days[0].Entries.Count);
        Assert.True(current.Days[0].Entries[0].IsIn);
    }

    [Fact]
    public async Task A_week_carries_on_from_the_start_of_the_accounts_month()
    {
        var (client, householdId) = await HouseholdAsync();

        // BUD-136: the week from 22 Nov brings forward the mortgage and council tax since the 1st, so the 25th shows
        // the same running total as the month does.
        var days = (await client.GetFromJsonAsync<CashFlowDaysResponse>(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 28)))))!;

        var joint = days.Accounts.Single(a => a.Name == "Joint");
        Assert.Equal((-1150m, (DateOnly?)new DateOnly(2026, 11, 1)), (joint.BroughtForward, joint.BroughtForwardSince));
        Assert.Equal(new[] { (new DateOnly(2026, 11, 25), -150m, true) }, joint.Days.Select(d => (d.Date, d.RunningTotal, d.IsShort)));
        Assert.Null(joint.MonthStartsWith);

        // It's short until the top-up arrives, so Cash flow suggests starting its month then.
        var month = (await client.GetFromJsonAsync<CashFlowDaysResponse>(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)))))!;
        var suggestion = month.Accounts.Single(a => a.Name == "Joint").Suggestion;
        Assert.NotNull(suggestion);
        Assert.Equal(("Bills top-up", new DateOnly(2026, 11, 25)), (suggestion.Name, suggestion.Date));
        Assert.Null(month.Accounts.Single(a => a.Name == "Current").Suggestion);
    }

    [Fact]
    public async Task An_accounts_month_can_start_when_its_money_arrives()
    {
        var (client, householdId) = await HouseholdAsync();
        var (jointId, billsId, topUpId, gymId) = (Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        await ExecuteDbContextAsync(async db =>
        {
            jointId = await db.Accounts.IgnoreQueryFilters().Where(a => a.HouseholdId == householdId && a.Name == "Joint").Select(a => a.Id).SingleAsync();
            billsId = await db.Accounts.IgnoreQueryFilters().Where(a => a.HouseholdId == householdId && a.Name == "Bills").Select(a => a.Id).SingleAsync();
            topUpId = await db.Transfers.IgnoreQueryFilters().Where(t => t.HouseholdId == householdId && t.Name == "Bills top-up").Select(t => t.Id).SingleAsync();
            gymId = await db.Outgoings.IgnoreQueryFilters().Where(o => o.HouseholdId == householdId && o.Name == "Gym").Select(o => o.Id).SingleAsync();
        });

        // The choices are the money into the account: the top-up into its Bills pot, not the move into its own Holiday pot.
        var accounts = (await client.GetFromJsonAsync<List<Budget.Shared.Accounts.AccountSummary>>(Url(Budget.Shared.Accounts.HouseholdAccountRoutes.Accounts(householdId))))!;
        Assert.Equal(new[] { "Bills top-up" }, accounts.Single(a => a.Id == jointId).MonthStartOptions.Select(o => o.Name));
        Assert.Empty(accounts.Single(a => a.Id == billsId).MonthStartOptions);

        using var notMoneyIn = await client.PutAsJsonAsync(Url(Budget.Shared.Accounts.HouseholdAccountRoutes.MonthStart(householdId, jointId)), new Budget.Shared.Accounts.SetMonthStartRequest { MonthStartsWithId = gymId });
        Assert.Equal(HttpStatusCode.BadRequest, notMoneyIn.StatusCode);
        using var pot = await client.PutAsJsonAsync(Url(Budget.Shared.Accounts.HouseholdAccountRoutes.MonthStart(householdId, billsId)), new Budget.Shared.Accounts.SetMonthStartRequest { MonthStartsWithId = topUpId });
        Assert.Equal(HttpStatusCode.NotFound, pot.StatusCode);

        using var set = await client.PutAsJsonAsync(Url(Budget.Shared.Accounts.HouseholdAccountRoutes.MonthStart(householdId, jointId)), new Budget.Shared.Accounts.SetMonthStartRequest { MonthStartsWithId = topUpId });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("Bills top-up", (await set.Content.ReadFromJsonAsync<Budget.Shared.Accounts.AccountSummary>())!.MonthStartsWithName);

        // November now carries on from the top-up on 25 Oct, and starts again when it arrives on 25 Nov.
        var days = (await client.GetFromJsonAsync<CashFlowDaysResponse>(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)))))!;
        var joint = days.Accounts.Single(a => a.Name == "Joint");
        Assert.Equal((1000m, (DateOnly?)new DateOnly(2026, 10, 25), "Bills top-up"), (joint.BroughtForward, joint.BroughtForwardSince, joint.MonthStartsWith));
        Assert.Equal(
            new (DateOnly, decimal, bool, DateOnly?)[] { (new DateOnly(2026, 11, 1), 0m, false, null), (new DateOnly(2026, 11, 5), -150m, true, null), (new DateOnly(2026, 11, 25), 1000m, false, new DateOnly(2026, 11, 25)) },
            joint.Days.Select(d => (d.Date, d.RunningTotal, d.IsShort, d.MonthStarted)));
        Assert.Null(joint.Suggestion);

        // A week across the top-up shows the same totals.
        var week = (await client.GetFromJsonAsync<CashFlowDaysResponse>(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 28)))))!;
        Assert.Equal((-150m, (DateOnly?)new DateOnly(2026, 10, 25)), (week.Accounts.Single(a => a.Name == "Joint").BroughtForward, week.Accounts.Single(a => a.Name == "Joint").BroughtForwardSince));
        Assert.Equal(1000m, week.Accounts.Single(a => a.Name == "Joint").Days.Single().RunningTotal);

        // Back to the 1st.
        using var reset = await client.PutAsJsonAsync(Url(Budget.Shared.Accounts.HouseholdAccountRoutes.MonthStart(householdId, jointId)), new Budget.Shared.Accounts.SetMonthStartRequest());
        Assert.Null((await reset.Content.ReadFromJsonAsync<Budget.Shared.Accounts.AccountSummary>())!.MonthStartsWithId);
    }

    [Fact]
    public async Task A_four_weekly_payment_starts_the_month_each_time_it_arrives()
    {
        var (client, householdId) = await HouseholdAsync();
        Guid currentId = Guid.Empty;
        await ExecuteDbContextAsync(async db =>
        {
            var person = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).Select(p => p.Id).FirstAsync();
            var current = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.HouseholdId == householdId && a.Name == "Current");
            currentId = current.Id;
            var pension = IncomeStream.Create(householdId, person, "State pension", 900m, new DateOnly(2026, 10, 12), "Start", new PaymentSchedule(4, PaymentUnit.Week));
            pension.PayInto(current.Id);
            db.IncomeStreams.Add(pension);
            current.StartMonthWith(pension.Id);
            await db.SaveChangesAsync();
        });

        // Paid on 12 Oct, 9 Nov and 7 Dec: November's days run on from 12 Oct, and start again on 9 Nov.
        var days = (await client.GetFromJsonAsync<CashFlowDaysResponse>(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)))))!;
        var current = days.Accounts.Single(a => a.AccountId == currentId);
        Assert.Equal(("State pension", (DateOnly?)new DateOnly(2026, 10, 12)), (current.MonthStartsWith, current.BroughtForwardSince));
        Assert.Equal(
            new (DateOnly, decimal, DateOnly?)[] { (new DateOnly(2026, 11, 9), 900m, new DateOnly(2026, 11, 9)), (new DateOnly(2026, 11, 25), 2900m, null) },
            current.Days.Select(d => (d.Date, d.RunningTotal, d.MonthStarted)));
    }

    [Fact]
    public async Task Savings_go_into_an_account_picked_or_typed_and_arrive_there_in_the_cash_flow()
    {
        var (client, householdId) = await HouseholdAsync();
        var category = Guid.Empty;
        await ExecuteDbContextAsync(async db => category = await db.OutgoingCategories.IgnoreQueryFilters()
            .Where(c => c.HouseholdId == householdId).Select(c => c.Id).FirstAsync());

        using var created = await client.PostAsJsonAsync(Url(Budget.Shared.Outgoings.OutgoingRoutes.Outgoings(householdId)), new Budget.Shared.Outgoings.CreateOutgoingRequest
        {
            Name = "House deposit",
            Kind = Budget.Shared.Outgoings.OutgoingKindDto.Savings,
            Priority = Budget.Shared.Outgoings.OutgoingPriorityDto.Preferred,
            CategoryId = category,
            Amount = 200m,
            Every = 1,
            Unit = Budget.Shared.Outgoings.PaymentUnitDto.Month,
            FirstPaymentDate = new DateOnly(2026, 1, 28),
            PaidFrom = "Current",
            Recipient = "Deposit ISA",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<Budget.Shared.Outgoings.OutgoingCreatedResponse>())!.Id;

        // A new name adds the account, and the outgoing shows it as where the money goes.
        var details = (await client.GetFromJsonAsync<Budget.Shared.Outgoings.OutgoingDetailsResponse>(Url(Budget.Shared.Outgoings.OutgoingRoutes.Outgoing(householdId, id))))!;
        Assert.Equal("Deposit ISA", details.Recipient);
        await ExecuteDbContextAsync(async db =>
        {
            var outgoing = await db.Outgoings.IgnoreQueryFilters().SingleAsync(o => o.Id == id);
            Assert.Equal((string?)null, outgoing.Recipient);
            Assert.Equal("Deposit ISA", (await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == outgoing.SavingsAccountId)).Name);
        });

        var month = (await client.GetFromJsonAsync<CashFlowMonthResponse>(Url(CashFlowRoutes.Month(householdId, 2026, 11))))!;
        Assert.Contains(month.Accounts.Single(a => a.Name == "Current").Out, e => e is { Kind: CashFlowKindDto.Outgoing, Name: "House deposit", Amount: 200m });
        var isa = month.Accounts.Single(a => a.Name == "Deposit ISA");
        Assert.Equal((200m, 0m), (isa.TotalIn, isa.TotalOut));
        Assert.Equal(CashFlowKindDto.SavingsIn, isa.In.Single().Kind);
    }

    [Fact]
    public async Task Only_while_transfers_are_on_and_for_the_household()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var off = await client.GetAsync(Url(CashFlowRoutes.Month(householdId, 2026, 11)));
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);

        using var tooLong = await client.GetAsync(Url(CashFlowRoutes.Days(householdId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1))));
        Assert.NotEqual(HttpStatusCode.OK, tooLong.StatusCode);

        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, h => CashFlowRoutes.Month(h, 2026, 11));
        await AssertMinimumRoleAsync(HttpMethod.Get, h => CashFlowRoutes.Month(h, 2026, 11), HouseholdRole.Viewer);
    }

    /// <summary>
    /// Salary of £3,000 into Current on the 25th; a £1,000 "Bills top-up" from Current into the Joint › Bills pot on
    /// the 25th; from Joint, a £1,000 mortgage (Bills pot) on the 1st and £150 council tax on the 5th; a £50 transfer
    /// from Joint into its own Holiday pot; and £20 a month with no account.
    /// </summary>
    private async Task<(HttpClient Client, Guid HouseholdId)> HouseholdAsync()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var client = CreateClient(signedInAs: ownerId);
        (await client.PutAsJsonAsync(Url(HouseholdRoutes.Settings(householdId)), new HouseholdSettingsRequest { TransfersEnabled = true })).Dispose();

        await ExecuteDbContextAsync(async db =>
        {
            var person = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).Select(p => p.Id).FirstAsync();
            var category = await db.OutgoingCategories.IgnoreQueryFilters().Where(c => c.HouseholdId == householdId).Select(c => c.Id).FirstAsync();

            var current = Account.Create(householdId, "Current", null, null);
            var joint = Account.Create(householdId, "Joint", null, null);
            var bills = Account.CreatePot(joint, "Bills");
            var holiday = Account.CreatePot(joint, "Holiday");
            db.Accounts.AddRange(current, joint, bills, holiday);

            var salary = IncomeStream.Create(householdId, person, "Salary", 3000m, new DateOnly(2026, 1, 25), "Start", PaymentSchedule.Monthly);
            salary.PayInto(current.Id);
            db.IncomeStreams.Add(salary);

            Outgoing Bill(string name, decimal amount, DateOnly first, Guid? accountId) => Outgoing.Create(
                householdId, new OutgoingDetails { Name = name, CategoryId = category, FirstPaymentDate = first, AccountId = accountId }, amount, PaymentSchedule.Monthly, "Start");
            db.Outgoings.AddRange(
                Bill("Mortgage", 1000m, new DateOnly(2026, 1, 1), bills.Id),
                Bill("Council tax", 150m, new DateOnly(2026, 1, 5), joint.Id),
                Bill("Gym", 20m, new DateOnly(2026, 1, 10), null));

            Transfer Move(string name, Guid from, Guid to, decimal amount) => Transfer.Create(
                householdId, new TransferDetails { Name = name, FromAccountId = from, ToAccountId = to, FirstPaymentDate = new DateOnly(2026, 1, 25) }, amount, PaymentSchedule.Monthly, "Start");
            db.Transfers.AddRange(Move("Bills top-up", current.Id, bills.Id, 1000m), Move("Holiday saving", joint.Id, holiday.Id, 50m));

            await db.SaveChangesAsync();
        });

        return (client, householdId);
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
