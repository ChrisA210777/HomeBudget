using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Activity;
using Budget.Shared.Outgoings;
using Budget.Shared.People;

namespace Budget.Server.Tests.Activity;

/// <summary>Recent activity (BUD-139): what's been changed in the household, and by whom.</summary>
[Collection(ApiCollection.Name)]
public class ActivityApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Each_change_is_recorded_with_who_made_it_newest_first()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);

        using var added = await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = "Sam" });
        var sam = (await added.Content.ReadFromJsonAsync<PersonSummary>())!;
        using var renamed = await client.PutAsJsonAsync(Url(PeopleRoutes.Person(householdId, sam.Id)), new SavePersonRequest { DisplayName = "Sammy" });

        var activity = (await client.GetFromJsonAsync<ActivityResponse>(Url(ActivityRoutes.Activity(householdId, take: 5))))!;

        Assert.Equal("GBP", activity.CurrencyCode);
        Assert.Equal(
            new[] { "Chris renamed Sam to Sammy", "Chris added Sam to the household's people" },
            activity.Items.Select(i => $"{i.Who} {i.Summary}"));
        Assert.All(activity.Items, i => Assert.Equal((ActivityAreaDto.People, (Guid?)sam.Id, true), (i.Area, i.SubjectId, i.Exists)));
    }

    [Fact]
    public async Task Repeated_edits_to_the_same_item_soon_after_are_one_entry()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);

        using var added = await client.PostAsJsonAsync(Url(OutgoingCategoryRoutes.Categories(householdId)), new AddCategoryRequest { Name = "Hobbies" });
        var category = (await added.Content.ReadFromJsonAsync<OutgoingCategorySummary>())!;
        foreach (var name in new[] { "Hobby", "Hobbies & crafts" })
        {
            using var updated = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, category.Id)), new UpdateCategoryRequest { Name = name });
            updated.EnsureSuccessStatusCode();
        }

        // Hiding it is a different kind of change, so it's an entry of its own.
        using var hidden = await client.PutAsJsonAsync(
            Url(OutgoingCategoryRoutes.Category(householdId, category.Id)), new UpdateCategoryRequest { Name = "Hobbies & crafts", IsHidden = true });

        var activity = (await client.GetFromJsonAsync<ActivityResponse>(Url(ActivityRoutes.Activity(householdId, take: 10))))!;
        Assert.Equal(
            new[] { "hid the category Hobbies & crafts", "updated the category Hobbies & crafts", "added the category Hobbies" },
            activity.Items.Select(i => i.Summary));
    }

    [Fact]
    public async Task Everyone_in_the_household_can_see_it_and_only_them()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer, "Jane Viewer");
        using var owner = CreateClient(signedInAs: ownerId);
        using var added = await owner.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = "Sam" });

        using var viewer = CreateClient(signedInAs: viewerId);
        var seen = (await viewer.GetFromJsonAsync<ActivityResponse>(Url(ActivityRoutes.Activity(householdId, take: 5))))!;
        Assert.Contains(seen.Items, i => i.Summary == "added Sam to the household's people");

        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, id => ActivityRoutes.Activity(id, take: 5));
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
