using Budget.Client.Account;
using Budget.Client.DataExport;
using Budget.Client.Pages.Households;
using Budget.Shared.DataExport;
using Microsoft.AspNetCore.Components.Forms;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Households;

public sealed class HouseholdDataTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly StubImportApi _importApi = new();

    public HouseholdDataTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IImportApi>(_importApi);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Admins_download_the_core_data_by_default_with_a_warning()
    {
        UseRole("Admin");

        var cut = _context.RenderComponent<HouseholdData>();

        var link = cut.Find("a[data-testid=download-json]");
        Assert.Equal($"/api/households/{FakeAccountApi.HouseholdId}/export?history=false&audit=false", link.GetAttribute("href"));
        Assert.True(link.HasAttribute("download"));
        Assert.Contains("personal and financial details", cut.Find("[data-testid=export-warning]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ticking_the_options_adds_history_and_audit_details()
    {
        UseRole("Admin");
        var cut = _context.RenderComponent<HouseholdData>();

        var boxes = cut.FindComponents<MudCheckBox<bool>>();
        await cut.InvokeAsync(() => boxes[0].Instance.ValueChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => boxes[1].Instance.ValueChanged.InvokeAsync(true));

        Assert.EndsWith("/export?history=true&audit=true", cut.Find("a[data-testid=download-json]").GetAttribute("href"), StringComparison.Ordinal);
        Assert.EndsWith("/export/csv?history=true&audit=true", cut.Find("a[data-testid=download-csv]").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Viewer")]
    public void Only_admins_can_download(string role)
    {
        UseRole(role, isOwner: false);

        var cut = _context.RenderComponent<HouseholdData>();

        Assert.NotNull(cut.Find("[data-testid=admins-only]"));
        Assert.Empty(cut.FindAll("[data-testid=export-section]"));
    }

    [Fact]
    public void Admins_see_who_has_downloaded_the_data()
    {
        UseRole("Admin");
        _importApi.Downloads.Add(new DataDownloadSummary(new DateTimeOffset(2026, 11, 13, 9, 30, 0, TimeSpan.Zero), "Chris Austin", true, "Csv", true, true));
        _importApi.Downloads.Add(new DataDownloadSummary(new DateTimeOffset(2026, 11, 2, 18, 5, 0, TimeSpan.Zero), "A former member", false, "Json", false, false));

        var cut = _context.RenderComponent<HouseholdData>();

        var entries = cut.FindAll("[data-testid=download]").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("You", entries[0].QuerySelector("[data-testid=download-who]")!.TextContent);
        Assert.Contains("CSV for Excel, with full history and who recorded what", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("A former member", entries[1].QuerySelector("[data-testid=download-who]")!.TextContent);
        Assert.Contains("JSON, current items", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("2026", entries[1].QuerySelector("[data-testid=download-when]")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Admins_are_told_when_no_one_has_downloaded_the_data()
    {
        UseRole("Admin");

        var cut = _context.RenderComponent<HouseholdData>();

        Assert.Contains("No one has downloaded the data in the last 12 months", cut.Find("[data-testid=downloads-none]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_who_arent_admins_dont_see_or_load_the_log()
    {
        UseRole("User", isOwner: false);

        var cut = _context.RenderComponent<HouseholdData>();

        Assert.Empty(cut.FindAll("[data-testid=downloads-section]"));
        Assert.Equal(0, _importApi.DownloadsRead);
    }

    [Fact]
    public void A_chosen_file_is_checked_and_summarised()
    {
        UseRole("Admin");
        _importApi.Preview = new ImportPreviewResponse
        {
            CanImport = true,
            FileVersion = 1,
            HouseholdName = "The Austins",
            CurrencyCode = "GBP",
            ExportedAt = new DateTimeOffset(2026, 11, 6, 17, 30, 0, TimeSpan.Zero),
            AppVersion = "1.0.0",
            IncludesHistory = true,
            Counts = new ImportCounts { People = 1, Categories = 16, Incomes = 2, Outgoings = 1, HistoryEntries = 5 },
            Members = [new ImportMember("Chris Austin", "chris@example.com", "Admin", true)],
        };
        var cut = _context.RenderComponent<HouseholdData>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{ \"format\": \"home-budget-export\" }", "backup.json"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=import-ok]")));
        Assert.Equal("{ \"format\": \"home-budget-export\" }", System.Text.Encoding.UTF8.GetString(_importApi.Uploaded!));
        Assert.Equal("The Austins (GBP)", cut.Find("[data-testid=import-household]").TextContent.Trim());
        Assert.Contains("1 person, 16 categories, 2 incomes and 1 outgoing", System.Text.RegularExpressions.Regex.Replace(cut.Find("[data-testid=import-counts]").TextContent, @"\s+", " "), StringComparison.Ordinal);
        Assert.Contains("Chris Austin (chris@example.com) – Owner", cut.Find("[data-testid=import-members]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_with_a_file_are_listed_with_where_they_are()
    {
        UseRole("Admin");
        _importApi.Preview = new ImportPreviewResponse
        {
            Errors = [new ImportProblem("outgoings[1].amountChanges[0].amount", "must be more than 0."), new ImportProblem(string.Empty, "Something general.")],
            Warnings = ["This file is from an older version of Home Budget (format version 1) and was upgraded."],
        };
        var cut = _context.RenderComponent<HouseholdData>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{}", "bad.json"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=import-errors]")));
        Assert.Equal(new[] { "outgoings[1].amountChanges[0].amount: must be more than 0.", "Something general." },
            cut.FindAll("[data-testid=import-error]").Select(e => e.TextContent.Trim()));
        Assert.NotNull(cut.Find("[data-testid=import-warning]"));
        Assert.Empty(cut.FindAll("[data-testid=import-ok]"));
    }

    [Fact]
    public void A_checked_file_can_be_imported_as_a_new_household_named_and_linked_to_you()
    {
        UseRole("Admin");
        var chris = Guid.NewGuid();
        _importApi.Preview = GoodPreview(new ImportPerson(Guid.NewGuid(), "Jane", "jane@example.com", false), new ImportPerson(chris, "Chris", "chris@outlook.com", true));
        var cut = _context.RenderComponent<HouseholdData>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{}", "backup.json"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=import-new-household]")));

        Assert.Equal("The Austins", cut.Find("input[data-testid=import-name]").GetAttribute("value"));
        cut.Find("input[data-testid=import-name]").Input("Austins (restored)");
        cut.Find("[data-testid=import-new-household]").Click();

        // The person the file links to your email is chosen for you.
        cut.WaitForAssertion(() => Assert.Equal(("Austins (restored)", (Guid?)chris), _importApi.Imported.Select(i => (i.Name, i.Me)).Single()));
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/?imported=true", navigation.Uri);
    }

    [Fact]
    public void A_failed_import_is_explained_and_nothing_moves_on()
    {
        UseRole("Admin");
        _importApi.Preview = GoodPreview();
        _importApi.Result = new ImportResultResponse { Errors = [new ImportProblem("household.currencyCode", "Only GBP households are supported at the moment.")] };
        var cut = _context.RenderComponent<HouseholdData>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{}", "backup.json"));
        cut.WaitForAssertion(() => cut.Find("[data-testid=import-new-household]").Click());

        cut.WaitForAssertion(() => Assert.Contains("Only GBP households", cut.Find("[data-testid=import-failed]").TextContent, StringComparison.Ordinal));
        Assert.Equal("http://localhost/", _context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Replacing_says_what_will_go_needs_the_name_typed_and_offers_a_backup()
    {
        UseRole("Admin");
        _importApi.Preview = GoodPreview() with
        {
            WouldDelete = new ImportCounts { People = 1, Categories = 16, Incomes = 2, Outgoings = 5 },
            PeopleKept = ["Chris"],
        };
        var cut = _context.RenderComponent<HouseholdData>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{}", "backup.json"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=replace-section]")));

        var deletes = System.Text.RegularExpressions.Regex.Replace(cut.Find("[data-testid=replace-deletes]").TextContent, @"\s+", " ");
        Assert.Contains("2 incomes, 5 outgoings (with their history), 16 categories and 1 person who don't sign in", deletes, StringComparison.Ordinal);
        Assert.Contains("(Chris)", deletes, StringComparison.Ordinal);
        Assert.Equal($"/api/households/{FakeAccountApi.HouseholdId}/export?history=true&audit=true", cut.Find("a[data-testid=replace-backup]").GetAttribute("href"));
        Assert.True(cut.Find("[data-testid=replace-data]").HasAttribute("disabled"));

        cut.Find("input[data-testid=replace-confirm]").Input(" my household ");
        cut.Find("[data-testid=replace-data]").Click();

        cut.WaitForAssertion(() => Assert.Equal((FakeAccountApi.HouseholdId, false), Assert.Single(_importApi.Replaced)));
        Assert.Equal("http://localhost/?replaced=true", _context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    private static ImportPreviewResponse GoodPreview(params ImportPerson[] people) => new()
    {
        CanImport = true,
        FileVersion = 1,
        HouseholdName = "The Austins",
        CurrencyCode = "GBP",
        People = people,
    };

    public void Dispose() => _context.Dispose();

    private sealed class StubImportApi : IImportApi
    {
        public ImportPreviewResponse Preview { get; set; } = new();

        public byte[]? Uploaded { get; private set; }

        public Task<ImportPreviewResponse> PreviewAsync(Guid householdId, byte[] file)
        {
            Uploaded = file;
            return Task.FromResult(Preview);
        }

        public List<(byte[] File, string? Name, Guid? Me)> Imported { get; } = [];

        public ImportResultResponse Result { get; set; } = new() { HouseholdId = Guid.NewGuid(), HouseholdName = "The Austins" };

        public Task<ImportResultResponse> ImportAsNewHouseholdAsync(Guid householdId, byte[] file, string? name, Guid? me)
        {
            Imported.Add((file, name, me));
            return Task.FromResult(Result);
        }

        public List<(Guid HouseholdId, bool UseFileName)> Replaced { get; } = [];

        public Task<ImportResultResponse> ReplaceAsync(Guid householdId, byte[] file, bool useFileName)
        {
            Replaced.Add((householdId, useFileName));
            return Task.FromResult(Result);
        }

        public List<DataDownloadSummary> Downloads { get; } = [];

        public int DownloadsRead { get; private set; }

        public Task<IReadOnlyList<DataDownloadSummary>> DownloadsAsync(Guid householdId)
        {
            DownloadsRead++;
            return Task.FromResult<IReadOnlyList<DataDownloadSummary>>(Downloads);
        }
    }

    private void UseRole(string role, bool isOwner = true)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
    }
}
