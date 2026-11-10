using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Accounts;
using Budget.Shared.Households;
using Budget.Shared.Outgoings;
using Budget.Shared.Transfers;
using Microsoft.AspNetCore.Mvc;

namespace Budget.Server.Tests.Transfers;

/// <summary>/api/households/{id}/transfers (BUD-91, ADR-014).</summary>
[Collection(ApiCollection.Name)]
public class TransferApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Transfers_are_listed_by_the_account_they_go_into()
    {
        var (client, householdId) = await OwnerAsync();
        var current = await AccountAsync(client, householdId, "Chris current");
        var joint = await AccountAsync(client, householdId, "Joint");
        var holiday = await AccountAsync(client, householdId, "Holiday", joint.Id);
        var savings = await AccountAsync(client, householdId, "Savings");

        await CreateAsync(client, householdId, Request(current.Id, joint.Id, 400m) with { Name = "Bills" });
        await CreateAsync(client, householdId, Request(current.Id, holiday.Id, 50m));
        await CreateAsync(client, householdId, Request(joint.Id, savings.Id, 100m));

        var list = (await client.GetFromJsonAsync<TransfersResponse>(Url(TransferRoutes.Transfers(householdId))))!;

        Assert.Equal(
            new[] { ("Joint", "Bills", "Chris current", "Joint"), ("Joint", null, "Chris current", "Joint › Holiday"), ("Savings", null, "Joint", "Savings") },
            list.Transfers.Select(t => (t.ToTopAccountName, t.Name, t.FromName, t.ToName)));
        Assert.Equal(400m, list.Transfers[0].MonthlyAmount);
    }

    [Fact]
    public async Task A_transfer_has_the_same_history_as_an_outgoing()
    {
        var (client, householdId) = await OwnerAsync();
        var current = await AccountAsync(client, householdId, "Current");
        var joint = await AccountAsync(client, householdId, "Joint");
        var created = await CreateAsync(client, householdId, Request(current.Id, joint.Id, 400m) with { FirstPaymentDate = new DateOnly(2026, 1, 25) });

        using var changed = await client.PostAsJsonAsync(Url(TransferRoutes.AmountChanges(householdId, created.Id)), new SaveOutgoingAmountRequest
        {
            Amount = 450m, Every = 1, Unit = PaymentUnitDto.Month, EffectiveFrom = new DateOnly(2026, 4, 25), Comment = "Council tax rise", Version = created.Version,
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var afterChange = (await changed.Content.ReadFromJsonAsync<TransferDetailsResponse>())!;

        using var paused = await client.PostAsJsonAsync(Url(TransferRoutes.StatusPeriods(householdId, created.Id)), new SaveOutgoingStatusRequest
        {
            Reason = OutgoingStatusReasonDto.Paused, StartDate = new DateOnly(2026, 6, 25), SkipPayments = 2, Comment = "Holiday", Version = afterChange.Version,
        });
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        var details = (await paused.Content.ReadFromJsonAsync<TransferDetailsResponse>())!;

        Assert.Equal(new[] { 450m, 400m }, details.AmountChanges.Select(c => c.Amount));
        Assert.Equal((new DateOnly(2026, 8, 25), (int?)2), (details.StatusPeriods.Single().ResumesOn, details.StatusPeriods.Single().SkippedPayments));

        // Saving over someone else's change is refused.
        using var stale = await client.PutAsJsonAsync(Url(TransferRoutes.Transfer(householdId, created.Id)), new UpdateTransferRequest
        {
            FromAccountId = current.Id, ToAccountId = joint.Id, Version = created.Version,
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Transfers_need_two_different_accounts_in_the_household()
    {
        var (client, householdId) = await OwnerAsync();
        var current = await AccountAsync(client, householdId, "Current");

        using var same = await client.PostAsJsonAsync(Url(TransferRoutes.Transfers(householdId)), Request(current.Id, current.Id, 10m));
        using var stranger = await client.PostAsJsonAsync(Url(TransferRoutes.Transfers(householdId)), Request(current.Id, Guid.NewGuid(), 10m));
        using var noAmount = await client.PostAsJsonAsync(Url(TransferRoutes.Transfers(householdId)), Request(current.Id, Guid.NewGuid(), 10m) with { Amount = null });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (same.StatusCode, stranger.StatusCode, noAmount.StatusCode));
        Assert.Contains(nameof(CreateTransferRequest.ToAccountId), (await stranger.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task With_transfers_turned_off_they_can_be_seen_but_not_changed()
    {
        var (client, householdId) = await OwnerAsync();
        var current = await AccountAsync(client, householdId, "Current");
        var joint = await AccountAsync(client, householdId, "Joint");
        var created = await CreateAsync(client, householdId, Request(current.Id, joint.Id, 10m));

        (await client.PutAsJsonAsync(Url(HouseholdRoutes.Settings(householdId)), new HouseholdSettingsRequest { TransfersEnabled = false })).Dispose();

        using var refused = await client.PostAsJsonAsync(Url(TransferRoutes.Transfers(householdId)), Request(current.Id, joint.Id, 10m));
        using var deleted = await client.DeleteAsync(Url(TransferRoutes.Transfer(householdId, created.Id)));
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (refused.StatusCode, deleted.StatusCode));
        Assert.Single((await client.GetFromJsonAsync<TransfersResponse>(Url(TransferRoutes.Transfers(householdId))))!.Transfers);
    }

    [Fact]
    public async Task Merging_accounts_moves_their_transfers_but_not_into_a_transfer_to_itself()
    {
        var (client, householdId) = await OwnerAsync();
        var current = await AccountAsync(client, householdId, "Current");
        var oldJoint = await AccountAsync(client, householdId, "Old joint");
        var joint = await AccountAsync(client, householdId, "Joint");
        await CreateAsync(client, householdId, Request(current.Id, oldJoint.Id, 10m));

        using var merged = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Merge(householdId, oldJoint.Id)), new MergeAccountRequest { IntoAccountId = joint.Id });
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        Assert.Equal("Joint", (await client.GetFromJsonAsync<TransfersResponse>(Url(TransferRoutes.Transfers(householdId))))!.Transfers.Single().ToName);

        using var refused = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Merge(householdId, current.Id)), new MergeAccountRequest { IntoAccountId = joint.Id });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Anyone_can_see_transfers_and_users_can_change_them()
    {
        await AssertMinimumRoleAsync(HttpMethod.Get, TransferRoutes.Transfers, HouseholdRole.Viewer);
        await AssertMinimumRoleAsync(HttpMethod.Post, TransferRoutes.Transfers, HouseholdRole.User, () => Request(Guid.NewGuid(), Guid.NewGuid(), 10m));
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, TransferRoutes.Transfers);
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, TransferRoutes.Transfers, Request(Guid.NewGuid(), Guid.NewGuid(), 10m));
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, h => TransferRoutes.Transfer(h, Guid.NewGuid()));
    }

    /// <summary>An owner whose household has "Accounts and transfers" on.</summary>
    private async Task<(HttpClient Client, Guid HouseholdId)> OwnerAsync()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var client = CreateClient(signedInAs: ownerId);
        (await client.PutAsJsonAsync(Url(HouseholdRoutes.Settings(householdId)), new HouseholdSettingsRequest { TransfersEnabled = true })).Dispose();
        return (client, householdId);
    }

    private static async Task<AccountSummary> AccountAsync(HttpClient client, Guid householdId, string name, Guid? parentId = null)
    {
        using var response = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Accounts(householdId)), new SaveAccountRequest { Name = name, ParentAccountId = parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountSummary>())!;
    }

    private static async Task<TransferDetailsResponse> CreateAsync(HttpClient client, Guid householdId, CreateTransferRequest request)
    {
        using var response = await client.PostAsJsonAsync(Url(TransferRoutes.Transfers(householdId)), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransferDetailsResponse>())!;
    }

    private static CreateTransferRequest Request(Guid from, Guid to, decimal? amount) => new()
    {
        FromAccountId = from,
        ToAccountId = to,
        Amount = amount,
        Every = 1,
        Unit = PaymentUnitDto.Month,
        FirstPaymentDate = new DateOnly(2026, 1, 25),
    };

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
