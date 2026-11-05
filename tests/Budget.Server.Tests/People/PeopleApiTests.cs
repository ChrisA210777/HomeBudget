using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Features.People;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.People;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.People;

[Collection(ApiCollection.Name)]
public class PeopleApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task New_owner_is_already_a_person_linked_to_their_login()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);

        var people = await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId)));

        var me = Assert.Single(people!);
        Assert.Equal("Chris Austin", me.DisplayName);
        Assert.Equal(ownerId, me.LinkedUserId);
        Assert.Equal("Chris Austin", me.LinkedUserName);
        Assert.True(me.IsYou);
    }

    [Fact]
    public async Task Current_user_includes_their_person_in_each_household()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var people = await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId)));

        var current = await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));

        Assert.Equal(people!.Single().Id, current!.Households.Single().PersonId);
    }

    [Fact]
    public async Task People_who_do_not_sign_in_can_be_added_and_renamed()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris");
        using var client = CreateClient(signedInAs: ownerId);

        using var added = await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = "  Jane  " });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var jane = (await added.Content.ReadFromJsonAsync<PersonSummary>())!;
        Assert.Equal("Jane", jane.DisplayName);
        Assert.Null(jane.LinkedUserId);
        Assert.False(jane.IsYou);

        using var renamed = await client.PutAsJsonAsync(Url(PeopleRoutes.Person(householdId, jane.Id)), new SavePersonRequest { DisplayName = "Jane Austin" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var people = await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId)));
        Assert.Equal(new[] { "Chris", "Jane Austin" }, people!.Select(p => p.DisplayName));
    }

    [Theory]
    [InlineData("Jane")]
    [InlineData("JANE")]
    [InlineData(" jane ")]
    public async Task Names_are_unique_within_a_household_ignoring_case(string duplicate)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris");
        using var client = CreateClient(signedInAs: ownerId);
        using var first = await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = "Jane" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var response = await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = duplicate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(PeopleEndpoints.DuplicateName, problem!.Errors[nameof(SavePersonRequest.DisplayName)].Single());
    }

    [Fact]
    public async Task The_same_name_can_be_used_in_different_households()
    {
        var (firstOwner, firstHousehold) = await CreateHouseholdOwnerAsync("Chris");
        var (secondOwner, secondHousehold) = await CreateHouseholdOwnerAsync("Sam");
        using var first = CreateClient(signedInAs: firstOwner);
        using var second = CreateClient(signedInAs: secondOwner);

        using var a = await first.PostAsJsonAsync(Url(PeopleRoutes.People(firstHousehold)), new SavePersonRequest { DisplayName = "Alex" });
        using var b = await second.PostAsJsonAsync(Url(PeopleRoutes.People(secondHousehold)), new SavePersonRequest { DisplayName = "Alex" });

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
    }

    [Fact]
    public async Task Renaming_to_your_own_name_is_allowed()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris");
        using var client = CreateClient(signedInAs: ownerId);
        var me = (await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId))))!.Single();

        using var response = await client.PutAsJsonAsync(Url(PeopleRoutes.Person(householdId, me.Id)), new SavePersonRequest { DisplayName = "CHRIS" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Blank_names_are_rejected()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_households_person_cannot_be_renamed_through_your_own_household()
    {
        var (otherOwner, otherHousehold) = await CreateHouseholdOwnerAsync("Other");
        using var otherClient = CreateClient(signedInAs: otherOwner);
        var theirs = (await otherClient.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(otherHousehold))))!.Single();

        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Me");
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PutAsJsonAsync(Url(PeopleRoutes.Person(householdId, theirs.Id)), new SavePersonRequest { DisplayName = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await ExecuteDbContextAsync(async db =>
            Assert.Equal("Other", await db.People.IgnoreQueryFilters().Where(p => p.Id == theirs.Id).Select(p => p.DisplayName).SingleAsync()));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("add")]
    [InlineData("rename")]
    public Task Other_households_cannot_use_any_people_endpoint(string endpoint)
    {
        var (method, _, body) = Endpoint(endpoint, Guid.NewGuid());
        return AssertOtherHouseholdsCannotAccessAsync(method, householdId => Endpoint(endpoint, householdId).Url, body);
    }

    [Fact]
    public Task Anyone_can_see_people() =>
        AssertMinimumRoleAsync(HttpMethod.Get, PeopleRoutes.People, HouseholdRole.Viewer);

    [Fact]
    public Task Adding_people_needs_User() =>
        AssertMinimumRoleAsync(
            HttpMethod.Post,
            PeopleRoutes.People,
            HouseholdRole.User,
            () => new SavePersonRequest { DisplayName = $"Person {Guid.NewGuid():N}" });

    [Fact]
    public async Task Viewers_cannot_rename_people()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var ownerClient = CreateClient(signedInAs: ownerId);
        var me = (await ownerClient.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId))))!.Single();
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var viewerClient = CreateClient(signedInAs: viewerId, followRedirects: false);

        using var response = await viewerClient.PutAsJsonAsync(Url(PeopleRoutes.Person(householdId, me.Id)), new SavePersonRequest { DisplayName = "Renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static (HttpMethod Method, string Url, object? Body) Endpoint(string name, Guid householdId) => name switch
    {
        "list" => (HttpMethod.Get, PeopleRoutes.People(householdId), null),
        "add" => (HttpMethod.Post, PeopleRoutes.People(householdId), new SavePersonRequest { DisplayName = "Someone" }),
        "rename" => (HttpMethod.Put, PeopleRoutes.Person(householdId, Guid.NewGuid()), new SavePersonRequest { DisplayName = "Someone" }),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
