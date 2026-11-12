using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Budget.Server.Features.SignIn;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Budgeting;
using Budget.Shared.DataExport;
using Budget.Shared.Households;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Budget.Server.Tests.Hosting;

/// <summary>
/// No names, emails, amounts, free text or invitation tokens in the logs (BUD-146). Runs the main flows with a
/// household whose every name and note is a distinctive made-up word, captures everything the app logs at the levels
/// sent to Application Insights, and fails if any of those words, amounts or tokens appear.
/// </summary>
[Collection(ApiCollection.Name)]
public class NoPersonalDataInLogsTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    /// <summary>Fields in an export that hold names or free text people typed.</summary>
    private static readonly HashSet<string> FreeTextFields =
    [
        "name", "displayName", "email", "linkedMemberEmail", "recordedBy", "changedBy", "provider", "paidInto", "paidFrom",
        "comment", "recipient", "referenceLabel", "referenceValue", "notes", "before", "after", "from", "to",
    ];

    [Fact]
    public async Task The_main_flows_log_no_personal_or_financial_data()
    {
        var logs = new CapturedLogs();
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        var sentinels = new List<string>();
        string Word()
        {
            var word = $"Qzvk{sentinels.Count + 1001}";
            sentinels.Add(word);
            return word;
        }

        // Signing up through the real first-sign-in path.
        var (ownerId, ownHouseholdId) = await SignUpAsync(app, Word(), $"{Word().ToLowerInvariant()}@example.com");
        using var owner = Client(app, ownerId);

        // Importing a household where every name and note is made up, as a new household with a made-up name.
        var (file, amounts) = MadeUpHousehold(Word);
        sentinels.AddRange(amounts);
        var imported = await PostFileAsync<ImportResultResponse>(owner, DataRoutes.ImportAsNewHousehold(ownHouseholdId, Word(), null), file);
        Assert.True(imported.Succeeded, string.Join("; ", imported.Errors.Select(e => e.Message)));
        var householdId = imported.HouseholdId!.Value;

        // Looking at the main pages.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var page in new[]
        {
            HouseholdRoutes.Setup(householdId), $"/api/households/{householdId}/people", $"/api/households/{householdId}/income-streams",
            $"/api/households/{householdId}/outgoings", $"/api/households/{householdId}/outgoings/coming-up",
            $"/api/households/{householdId}/outgoing-categories", $"/api/households/{householdId}/accounts",
            $"/api/households/{householdId}/transfers", BudgetRoutes.Month(householdId),
            $"/api/households/{householdId}/cash-flow/month?year={today.Year}&month={today.Month}",
            $"/api/households/{householdId}/activity", MemberRoutes.Members(householdId),
        })
        {
            using var response = await owner.GetAsync(Url(page));
        }

        // Inviting someone, who looks at the invitation and accepts it.
        var inviteeEmail = $"{Word().ToLowerInvariant()}@example.com";
        using (var invalid = await owner.PostAsJsonAsync(Url(InvitationRoutes.Invitations(householdId)), new CreateInvitationRequest { Email = Word() + " is not an email" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var invited = await owner.PostAsJsonAsync(Url(InvitationRoutes.Invitations(householdId)), new CreateInvitationRequest { Email = inviteeEmail, Role = "User" });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var link = (await invited.Content.ReadFromJsonAsync<InvitationSummary>())!.Link!;
        var token = Uri.UnescapeDataString(link[(link.LastIndexOf('/') + 1)..]);
        sentinels.Add(token);
        var (inviteeId, _) = await SignUpAsync(app, Word(), inviteeEmail);
        using var invitee = Client(app, inviteeId);
        using (var preview = await invitee.GetAsync(Url(InvitationRoutes.ByToken(token))))
        using (var accepted = await invitee.PostAsync(Url(InvitationRoutes.Accept(token)), null))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.True(accepted.IsSuccessStatusCode, $"Accepting gave {accepted.StatusCode}");
        }

        // Downloading the data, and checking the download as an import.
        var export = await owner.GetByteArrayAsync(Url(DataRoutes.Export(householdId, true, true)));
        _ = await owner.GetByteArrayAsync(Url(DataRoutes.ExportCsv(householdId, true, true)));
        _ = await PostFileAsync<ImportPreviewResponse>(owner, DataRoutes.Preview(householdId), export);

        var leaks = logs.Entries
            .SelectMany(entry => sentinels
                .Where(word => entry.Text.Contains(word, StringComparison.OrdinalIgnoreCase))
                .Select(word => $"\"{word}\" in [{entry.Level}] {entry.Category}: {entry.Text}"))
            .ToList();
        Assert.True(logs.Entries.Count > 0, "Nothing was logged, so the check can't have worked.");
        Assert.True(leaks.Count == 0, "Personal or financial data was logged:" + Environment.NewLine + string.Join(Environment.NewLine, leaks));
    }

    /// <summary>The golden export with every name and note replaced by a made-up word, and the amounts it holds.</summary>
    private static (byte[] File, List<string> Amounts) MadeUpHousehold(Func<string> word)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ImportCompatibility", "v5", "full-history-and-audit.json")))!;
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var amounts = new HashSet<decimal>();

        string Replace(string value) =>
            replacements.TryGetValue(value, out var replacement)
                ? replacement
                : replacements[value] = value.Contains('@', StringComparison.Ordinal) ? $"{word().ToLowerInvariant()}@example.com" : word();

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj.ToList())
                    {
                        if (value is JsonValue leaf && leaf.TryGetValue<string>(out var text) && FreeTextFields.Contains(key))
                        {
                            // A pot is named after its account: "Joint account › Holiday".
                            obj[key] = string.Join(" › ", text.Split(" › ").Select(Replace));
                        }
                        else if (value is JsonValue number && key.Contains("amount", StringComparison.OrdinalIgnoreCase)
                            && number.TryGetValue<decimal>(out var amount))
                        {
                            amounts.Add(amount);
                        }
                        else
                        {
                            Walk(value);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Walk(item);
                    }

                    break;
            }
        }

        Walk(json);

        // As they'd be written in a message: "102.20" and "£1,050.00". Small round amounts like 25 are too common to check.
        var uk = CultureInfo.GetCultureInfo("en-GB");
        var amountTexts = amounts.Where(a => a >= 100 || a != decimal.Truncate(a))
            .SelectMany(a => new[] { a.ToString("0.00", CultureInfo.InvariantCulture), a.ToString("C", uk) })
            .Distinct()
            .ToList();
        return (Encoding.UTF8.GetBytes(json.ToJsonString()), amountTexts);
    }

    private static async Task<(Guid UserId, Guid HouseholdId)> SignUpAsync(WebApplicationFactory<Program> app, string name, string email)
    {
        var key = Guid.NewGuid().ToString("N");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, key), new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Email, email)], "Microsoft"));

        await using var scope = app.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<ExternalUserProvisioner>()
            .FindOrCreateAsync(new ExternalLoginInfo(principal, "Microsoft", key, "Microsoft"));
        Assert.NotNull(user?.LastActiveHouseholdId);
        return (user.Id, user.LastActiveHouseholdId.Value);
    }

    private static HttpClient Client(WebApplicationFactory<Program> app, Guid userId)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiRoutes.CsrfHeaderName, "1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static async Task<T> PostFileAsync<T>(HttpClient client, string url, byte[] file)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(Url(url), content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private sealed record Entry(LogLevel Level, string Category, string Text);

    /// <summary>Everything logged, at the levels configured for the app (the same as sent to Application Insights).</summary>
    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Structured values are sent as well as the message, so they're checked too.
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                entries.Enqueue(new Entry(logLevel, category, $"{formatter(state, exception)} {values} {exception}"));
            }
        }
    }
}
