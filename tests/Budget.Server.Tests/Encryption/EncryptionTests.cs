using System.Net;
using System.Net.Http.Json;
using Budget.Infrastructure.Encryption;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Encryption;

/// <summary>Names and free text are stored encrypted (BUD-166, ADR-017).</summary>
[Collection(ApiCollection.Name)]
public class EncryptionTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Names_are_unreadable_in_the_database_but_read_normally_in_the_app()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);

        using var added = await client.PostAsJsonAsync(new Uri(PeopleRoutes.People(householdId), UriKind.Relative), new SavePersonRequest { DisplayName = "Sam Taylor" });
        added.EnsureSuccessStatusCode();
        var person = (await added.Content.ReadFromJsonAsync<PersonSummary>())!;

        var stored = await RawAsync($"SELECT DisplayName AS Value FROM People WHERE Id = '{person.Id}'");
        Assert.StartsWith(FieldEncryption.Prefix, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam", stored, StringComparison.Ordinal);
        Assert.Equal(FieldEncryption.NameKey("sam taylor"), await RawAsync($"SELECT NameKey AS Value FROM People WHERE Id = '{person.Id}'"));

        var people = await client.GetFromJsonAsync<List<PersonSummary>>(new Uri(PeopleRoutes.People(householdId), UriKind.Relative));
        Assert.Contains(people!, p => p.DisplayName == "Sam Taylor");
    }

    [Fact]
    public async Task Names_are_still_unique_ignoring_case()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);
        var url = new Uri(PeopleRoutes.People(householdId), UriKind.Relative);
        (await client.PostAsJsonAsync(url, new SavePersonRequest { DisplayName = "Jane" })).EnsureSuccessStatusCode();

        using var again = await client.PostAsJsonAsync(url, new SavePersonRequest { DisplayName = " JANE " });

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Data_stored_before_encryption_is_encrypted_when_the_app_starts()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var personId = Guid.NewGuid();
        await ExecuteDbContextAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO People (Id, HouseholdId, DisplayName, CreatedAt, ModifiedAt)
            VALUES ({personId}, {householdId}, N'Old Name', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """));

        await ExecuteDbContextAsync(async db => Assert.True(await ExistingDataEncryptor.RunAsync(db) >= 1));

        var stored = await RawAsync($"SELECT DisplayName AS Value FROM People WHERE Id = '{personId}'");
        Assert.True(FieldEncryption.IsEncrypted(stored));
        Assert.Equal("Old Name", FieldEncryption.Decrypt(stored));
        Assert.Equal(FieldEncryption.NameKey("Old Name"), await RawAsync($"SELECT NameKey AS Value FROM People WHERE Id = '{personId}'"));

        // Nothing is left to do the second time.
        await ExecuteDbContextAsync(async db => Assert.Equal(0, await ExistingDataEncryptor.RunAsync(db)));
    }

    private async Task<string> RawAsync(string sql)
    {
        string? value = null;
        await ExecuteDbContextAsync(async db => value = await db.Database.SqlQueryRaw<string>(sql).SingleAsync());
        return value!;
    }
}
