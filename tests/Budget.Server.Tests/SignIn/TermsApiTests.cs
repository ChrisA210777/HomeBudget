using System.Net;
using System.Net.Http.Json;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.SignIn;

/// <summary>Accepting the terms of use (BUD-152).</summary>
[Collection(ApiCollection.Name)]
public class TermsApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_new_user_must_accept_the_current_terms_and_then_needn_t_again()
    {
        var (userId, _) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);

        var before = (await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser)))!;
        using var accepted = await client.PostAsJsonAsync(Url(AccountRoutes.AcceptTerms), new AcceptTermsRequest { Version = LegalDocuments.TermsVersion });
        var after = (await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser)))!;

        Assert.True(before.MustAcceptTerms);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.False(after.MustAcceptTerms);
        await ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.Equal(LegalDocuments.TermsVersion, user.TermsAcceptedVersion);
            Assert.NotNull(user.TermsAcceptedAt);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.0")]
    public async Task Only_the_current_version_can_be_accepted(string? version)
    {
        var (userId, _) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);

        using var response = await client.PostAsJsonAsync(Url(AccountRoutes.AcceptTerms), new AcceptTermsRequest { Version = version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var user = (await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser)))!;
        Assert.True(user.MustAcceptTerms);
    }

    [Fact]
    public async Task Accepting_needs_you_to_be_signed_in()
    {
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync(Url(AccountRoutes.AcceptTerms), new AcceptTermsRequest { Version = LegalDocuments.TermsVersion });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
