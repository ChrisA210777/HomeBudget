using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Income;
using Microsoft.AspNetCore.Http;

namespace Budget.Server.Tests.Income;

[Collection(ApiCollection.Name)]
public class IncomeStreamApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly SaveEmploymentIncomeRequest FixedSalary = new()
    {
        Name = "Sam – Acme Ltd",
        EmployerName = "Acme Ltd",
        PayFrequency = PayFrequencyDto.FourWeekly,
        Kind = IncomeKindDto.Fixed,
        GrossAmount = 30000m,
        AmountBasis = AmountBasisDto.Annual,
    };

    private static readonly SaveEmploymentIncomeRequest VariablePay = new()
    {
        Name = "Overtime",
        PayFrequency = PayFrequencyDto.Weekly,
        Kind = IncomeKindDto.Variable,
        LowestGross = 300m,
        AverageGross = 450m,
        HighestGross = 700m,
    };

    [Fact]
    public async Task Adding_fixed_pay_returns_the_details_as_entered()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), FixedSalary with { MemberUserId = ownerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<EmploymentIncomeDetails>();
        Assert.NotNull(created);
        Assert.Equal("Sam – Acme Ltd", created.Name);
        Assert.Equal("Acme Ltd", created.EmployerName);
        Assert.Equal(ownerId, created.MemberUserId);
        Assert.Equal(PayFrequencyDto.FourWeekly, created.PayFrequency);
        Assert.Equal(IncomeKindDto.Fixed, created.Kind);
        Assert.Equal(30000m, created.GrossAmount);
        Assert.Equal(AmountBasisDto.Annual, created.AmountBasis);
        Assert.True(created.IsActive);
        Assert.False(string.IsNullOrEmpty(created.Version));
        Assert.Equal(IncomeRoutes.IncomeStream(householdId, created.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task List_shows_per_pay_and_annual_figures_and_totals()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Sam Owner");
        using var client = CreateClient(signedInAs: ownerId);
        await CreateAsync(client, householdId, FixedSalary with { MemberUserId = ownerId });
        await CreateAsync(client, householdId, VariablePay);

        var list = await client.GetFromJsonAsync<IncomeStreamListResponse>(Url(IncomeRoutes.IncomeStreams(householdId)));

        Assert.NotNull(list);
        Assert.Equal("GBP", list.CurrencyCode);
        Assert.Equal(new[] { "Overtime", "Sam – Acme Ltd" }, list.Items.Select(i => i.Name));

        var salary = list.Items.Single(i => i.Name == "Sam – Acme Ltd");
        Assert.Equal(2307.69m, salary.AveragePerPay);
        Assert.Equal(30000m, salary.AverageAnnual);
        Assert.Equal("Sam Owner", salary.MemberName);

        var overtime = list.Items.Single(i => i.Name == "Overtime");
        Assert.Equal(300m, overtime.LowPerPay);
        Assert.Equal(450m, overtime.AveragePerPay);
        Assert.Equal(700m, overtime.HighPerPay);
        Assert.Equal(23400m, overtime.AverageAnnual);
        Assert.Null(overtime.MemberName);

        // Variable pay counts at its average.
        Assert.Equal(53400m, list.TotalAnnual);
        Assert.Equal(4450m, list.TotalMonthly);
    }

    [Fact]
    public async Task Invalid_details_are_rejected_with_field_errors()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(
            Url(IncomeRoutes.IncomeStreams(householdId)),
            VariablePay with { Name = " ", LowestGross = 500m, AverageGross = 450m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(SaveEmploymentIncomeRequest.Name), problem.Errors.Keys);
        Assert.Contains(nameof(SaveEmploymentIncomeRequest.LowestGross), problem.Errors.Keys);
    }

    [Fact]
    public async Task Income_can_only_belong_to_a_member_of_the_household()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var (outsiderId, _) = await CreateHouseholdOwnerAsync("Outsider");
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(
            Url(IncomeRoutes.IncomeStreams(householdId)), FixedSalary with { MemberUserId = outsiderId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(nameof(SaveEmploymentIncomeRequest.MemberUserId), problem!.Errors.Keys);
    }

    [Fact]
    public async Task Editing_shows_values_as_entered_and_can_switch_to_variable_pay()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var created = await CreateAsync(client, householdId, FixedSalary);

        var loaded = await client.GetFromJsonAsync<EmploymentIncomeDetails>(Url(IncomeRoutes.IncomeStream(householdId, created.Id)));
        Assert.NotNull(loaded);
        Assert.Equal(30000m, loaded.GrossAmount);
        Assert.Equal(AmountBasisDto.Annual, loaded.AmountBasis);

        using var response = await client.PutAsJsonAsync(
            Url(IncomeRoutes.IncomeStream(householdId, created.Id)),
            VariablePay with { Name = "Sam – Acme Ltd", Version = loaded.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<EmploymentIncomeDetails>();
        Assert.NotNull(updated);
        Assert.Equal(IncomeKindDto.Variable, updated.Kind);
        Assert.Null(updated.GrossAmount);
        Assert.Null(updated.AmountBasis);
        Assert.Equal(450m, updated.AverageGross);
        Assert.NotEqual(loaded.Version, updated.Version);
    }

    [Fact]
    public async Task Saving_over_someone_elses_change_is_rejected()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var created = await CreateAsync(client, householdId, FixedSalary);
        var url = Url(IncomeRoutes.IncomeStream(householdId, created.Id));

        using var first = await client.PutAsJsonAsync(url, FixedSalary with { GrossAmount = 31000m, Version = created.Version });
        using var second = await client.PutAsJsonAsync(url, FixedSalary with { GrossAmount = 32000m, Version = created.Version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var saved = await client.GetFromJsonAsync<EmploymentIncomeDetails>(url);
        Assert.Equal(31000m, saved!.GrossAmount);
    }

    [Fact]
    public async Task Deactivated_income_is_hidden_by_default_and_left_out_of_totals()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var salary = await CreateAsync(client, householdId, FixedSalary);
        await CreateAsync(client, householdId, VariablePay);

        using var deactivate = await client.PostAsync(Url(IncomeRoutes.Deactivate(householdId, salary.Id)), content: null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var activeOnly = await client.GetFromJsonAsync<IncomeStreamListResponse>(Url(IncomeRoutes.IncomeStreams(householdId)));
        var everything = await client.GetFromJsonAsync<IncomeStreamListResponse>(Url(IncomeRoutes.IncomeStreams(householdId) + "?includeInactive=true"));

        Assert.Equal(new[] { "Overtime" }, activeOnly!.Items.Select(i => i.Name));
        Assert.Equal(23400m, activeOnly.TotalAnnual);
        Assert.Equal(2, everything!.Items.Count);
        Assert.False(everything.Items.Single(i => i.Id == salary.Id).IsActive);
        Assert.Equal(23400m, everything.TotalAnnual);

        using var reactivate = await client.PostAsync(Url(IncomeRoutes.Reactivate(householdId, salary.Id)), content: null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        var afterReactivate = await client.GetFromJsonAsync<IncomeStreamListResponse>(Url(IncomeRoutes.IncomeStreams(householdId)));
        Assert.Equal(53400m, afterReactivate!.TotalAnnual);
    }

    [Fact]
    public async Task Deleted_income_is_gone()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var created = await CreateAsync(client, householdId, FixedSalary);
        var url = Url(IncomeRoutes.IncomeStream(householdId, created.Id));

        using var delete = await client.DeleteAsync(url);
        using var get = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Another_households_income_stream_cannot_be_reached_through_your_own_household()
    {
        // Even with a household you belong to in the URL, the query filter hides other households' streams.
        var (otherOwnerId, otherHouseholdId) = await CreateHouseholdOwnerAsync("Other owner");
        using var otherClient = CreateClient(signedInAs: otherOwnerId);
        var theirs = await CreateAsync(otherClient, otherHouseholdId, FixedSalary);

        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var url = Url(IncomeRoutes.IncomeStream(householdId, theirs.Id));

        using var get = await client.GetAsync(url);
        using var put = await client.PutAsJsonAsync(url, FixedSalary with { Version = theirs.Version });
        using var deactivate = await client.PostAsync(Url(IncomeRoutes.Deactivate(householdId, theirs.Id)), content: null);
        using var delete = await client.DeleteAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var stillThere = await otherClient.GetFromJsonAsync<EmploymentIncomeDetails>(Url(IncomeRoutes.IncomeStream(otherHouseholdId, theirs.Id)));
        Assert.True(stillThere!.IsActive);
    }

    // Isolation and roles for every endpoint (see Features/README.md).

    public static TheoryData<string> Endpoints => new() { "list", "get", "create", "update", "deactivate", "reactivate", "delete" };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Other_households_cannot_use_any_income_endpoint(string endpoint)
    {
        var (method, _, body) = Endpoint(endpoint, Guid.NewGuid());

        await AssertOtherHouseholdsCannotAccessAsync(method, householdId => Endpoint(endpoint, householdId).Url, body);
    }

    [Theory]
    [InlineData("list", HouseholdRole.Viewer)]
    [InlineData("create", HouseholdRole.User)]
    public async Task Reading_needs_Viewer_and_changing_needs_User(string endpoint, HouseholdRole minimumRole)
    {
        var (method, _, body) = Endpoint(endpoint, Guid.Empty);

        await AssertMinimumRoleAsync(method, householdId => Endpoint(endpoint, householdId).Url, minimumRole, () => body);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    [InlineData("delete")]
    public async Task Viewers_cannot_change_existing_income(string endpoint)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var ownerClient = CreateClient(signedInAs: ownerId);
        var created = await CreateAsync(ownerClient, householdId, FixedSalary);
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var viewerClient = CreateClient(signedInAs: viewerId, followRedirects: false);
        var (method, url, body) = Endpoint(endpoint, householdId, created.Id, created.Version);

        using var request = new HttpRequestMessage(method, Url(url));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await viewerClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static (HttpMethod Method, string Url, object? Body) Endpoint(
        string name, Guid householdId, Guid? incomeStreamId = null, string? version = null)
    {
        var id = incomeStreamId ?? Guid.NewGuid();
        return name switch
        {
            "list" => (HttpMethod.Get, IncomeRoutes.IncomeStreams(householdId), null),
            "get" => (HttpMethod.Get, IncomeRoutes.IncomeStream(householdId, id), null),
            "create" => (HttpMethod.Post, IncomeRoutes.IncomeStreams(householdId), FixedSalary),
            "update" => (HttpMethod.Put, IncomeRoutes.IncomeStream(householdId, id), FixedSalary with { Version = version ?? "AAAAAAAAAAA=" }),
            "deactivate" => (HttpMethod.Post, IncomeRoutes.Deactivate(householdId, id), null),
            "reactivate" => (HttpMethod.Post, IncomeRoutes.Reactivate(householdId, id), null),
            "delete" => (HttpMethod.Delete, IncomeRoutes.IncomeStream(householdId, id), null),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    private static async Task<EmploymentIncomeDetails> CreateAsync(HttpClient client, Guid householdId, SaveEmploymentIncomeRequest request)
    {
        using var response = await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EmploymentIncomeDetails>())!;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
