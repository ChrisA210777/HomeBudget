using System.Net;
using System.Net.Http.Json;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;

namespace Budget.Server.Tests.SignIn;

/// <summary>GET and PUT /bff/user/settings (BUD-87).</summary>
[Collection(ApiCollection.Name)]
public class UserSettingsTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Uri Route = new(AccountRoutes.Settings, UriKind.Relative);

    [Fact]
    public async Task A_new_user_has_the_defaults()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);

        var settings = await client.GetFromJsonAsync<UserSettings>(Route);

        Assert.Equal(UserSettings.Defaults, settings);
        Assert.Equal(DayOfWeek.Sunday, settings!.FirstDayOfWeek);
    }

    [Fact]
    public async Task Changes_are_saved_and_anything_left_out_is_kept()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);

        using var first = await client.PutAsJsonAsync(Route, new UpdateUserSettingsRequest { FirstDayOfWeek = DayOfWeek.Monday, OutgoingsPeriod = ListPeriodDto.Year });
        using var second = await client.PutAsJsonAsync(Route, new UpdateUserSettingsRequest { OutgoingsGrouping = OutgoingsGroupingDto.PaidFrom });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var expected = new UserSettings(DayOfWeek.Monday, ListPeriodDto.Year, OutgoingsGroupingDto.PaidFrom, ListPeriodDto.Month);
        Assert.Equal(expected, await second.Content.ReadFromJsonAsync<UserSettings>());

        // From another device: a new session for the same user.
        using var elsewhere = CreateClient(signedInAs: user.Id);
        Assert.Equal(expected, await elsewhere.GetFromJsonAsync<UserSettings>(Route));
    }

    [Fact]
    public async Task Each_user_has_their_own_settings()
    {
        var chris = await CreateUserAsync();
        var jane = await CreateUserAsync();
        using var chrisClient = CreateClient(signedInAs: chris.Id);
        using var janeClient = CreateClient(signedInAs: jane.Id);

        using var response = await chrisClient.PutAsJsonAsync(Route, new UpdateUserSettingsRequest { IncomePeriod = ListPeriodDto.Year });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserSettings.Defaults, await janeClient.GetFromJsonAsync<UserSettings>(Route));
    }

    [Fact]
    public async Task Values_that_arent_allowed_are_rejected()
    {
        var user = await CreateUserAsync();
        using var client = CreateClient(signedInAs: user.Id);

        using var outOfRange = await client.PutAsJsonAsync(Route, new { firstDayOfWeek = 9, outgoingsPeriod = 7 });
        using var unknown = await client.PutAsJsonAsync(Route, new { outgoingsGrouping = "Colour" });

        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        var problem = await outOfRange.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(new[] { "FirstDayOfWeek", "OutgoingsPeriod" }, problem!.Errors.Keys.Order());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(UserSettings.Defaults, await client.GetFromJsonAsync<UserSettings>(Route));
    }

    [Fact]
    public async Task Nobody_signed_in_gets_401()
    {
        using var client = CreateClient(followRedirects: false);

        using var get = await client.GetAsync(Route);
        using var put = await client.PutAsJsonAsync(Route, new UpdateUserSettingsRequest { FirstDayOfWeek = DayOfWeek.Monday });

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (get.StatusCode, put.StatusCode));
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
