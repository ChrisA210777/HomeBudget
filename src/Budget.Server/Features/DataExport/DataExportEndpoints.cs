using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Budget.Domain.Households;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.DataExport;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.DataExport;

public static class DataExportEndpoints
{
    public static IEndpointRouteBuilder MapDataExportEndpoints(this IEndpointRouteBuilder app)
    {
        var household = app.MapGroup(HouseholdRoutes.Base);

        // Admins only: the file holds everyone's personal and financial details (ADR-012).
        household.MapGet("/export", ExportAsync).RequireAuthorization(HouseholdPolicies.Admin);
        household.MapPost("/import/preview", PreviewAsync).RequireAuthorization(HouseholdPolicies.Admin);

        // Admins of the household they're in (confirmed 6 Nov 2026); they become the new household's owner.
        household.MapPost("/import/new-household", ImportAsNewHouseholdAsync).RequireAuthorization(HouseholdPolicies.Admin);

        return app;
    }

    /// <summary>Downloads the household's data as a JSON file (BUD-65).</summary>
    private static async Task<IResult> ExportAsync(Guid householdId, bool? history, bool? audit, ExportBuilder builder, TimeProvider time)
    {
        var export = await builder.BuildAsync(householdId, new ExportOptions { IncludeHistory = history == true, IncludeAudit = audit == true });
        var json = JsonSerializer.SerializeToUtf8Bytes(export, ExportFormat.JsonOptions);
        var fileName = $"home-budget-{FileNamePart(export.Household.Name)}-{time.GetUtcNow():yyyy-MM-dd}.json";

        return Results.File(json, "application/json", fileName);
    }

    /// <summary>
    /// Checks an uploaded file and says what's in it (BUD-67). Nothing is saved, and the file isn't kept: it's read
    /// from the request, checked against the format and every domain rule as a dry run, and dropped.
    /// </summary>
    private static async Task<ImportPreviewResponse> PreviewAsync(Guid householdId, HttpRequest request, ClaimsPrincipal principal, BudgetDbContext db)
    {
        var email = await EmailOfAsync(principal, db);
        var file = await ReadFileAsync(request);
        if (file is null)
        {
            return Refused($"The file is too big. Exports are at most {ExportFormat.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var read = ExportReader.Read(file);
        if (read.Document is not { } export)
        {
            return new ImportPreviewResponse
            {
                Errors = read.Errors.Select(Problem).ToList(),
                Warnings = read.Warnings,
                FileVersion = read.FileVersion,
            };
        }

        var (_, problems) = ImportPlanner.Plan(export, householdId);
        return new ImportPreviewResponse
        {
            CanImport = problems.Count == 0,
            Errors = problems.Select(Problem).ToList(),
            Warnings = read.Warnings,
            FileVersion = read.FileVersion,
            HouseholdName = export.Household.Name,
            CurrencyCode = export.Household.CurrencyCode,
            ExportedAt = export.ExportedAt,
            AppVersion = export.AppVersion,
            IncludesHistory = export.Options.IncludeHistory,
            IncludesAudit = export.Options.IncludeAudit,
            Counts = new ImportCounts
            {
                People = export.People.Count,
                Categories = export.Categories.Count,
                Incomes = export.Incomes.Count,
                Outgoings = export.Outgoings.Count,
                HistoryEntries =
                    export.Incomes.Sum(i => i.AmountChanges.Count + i.StatusPeriods.Count + i.Corrections.Count)
                    + export.Outgoings.Sum(o => o.AmountChanges.Count + o.StatusPeriods.Count + o.Corrections.Count),
            },
            Members = export.Members.Select(m => new ImportMember(m.DisplayName, m.Email, m.Role, m.IsOwner)).ToList(),
            People = export.People
                .Select(p => new ImportPerson(p.Id, p.DisplayName, p.LinkedMemberEmail, IsSameEmail(p.LinkedMemberEmail, email)))
                .ToList(),
        };

        static ImportProblem Problem(ExportProblem p) => new(p.Path, p.Message);

        static ImportPreviewResponse Refused(string message) => new() { Errors = [new ImportProblem(string.Empty, message)] };
    }

    /// <summary>
    /// Imports a file as a new household (BUD-63), in one save: the household (owned by the caller, who becomes an
    /// Admin of it), and its people, categories, incomes and outgoings with new ids. Who first recorded each item is
    /// kept for information. Members aren't recreated: they're invited again. The new household becomes the caller's
    /// active one.
    /// </summary>
    private static async Task<ImportResultResponse> ImportAsNewHouseholdAsync(
        HttpRequest request, string? name, Guid? me, ClaimsPrincipal principal, BudgetDbContext db, TimeProvider time)
    {
        var file = await ReadFileAsync(request);
        if (file is null)
        {
            return Failed(string.Empty, $"The file is too big. Exports are at most {ExportFormat.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var read = ExportReader.Read(file);
        if (read.Document is not { } export)
        {
            return new ImportResultResponse { Errors = read.Errors.Select(p => new ImportProblem(p.Path, p.Message)).ToList() };
        }

        if (export.Household.CurrencyCode != Household.DefaultCurrencyCode)
        {
            return Failed("household.currencyCode", $"Only {Household.DefaultCurrencyCode} households are supported at the moment.");
        }

        var householdName = string.IsNullOrWhiteSpace(name) ? export.Household.Name : name;
        if (HouseholdValidator.Validate(new CreateHouseholdRequest { Name = householdName }).Values.SelectMany(v => v).FirstOrDefault() is { } nameError)
        {
            return Failed("name", nameError);
        }

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.SingleAsync(u => u.Id == userId);

        var household = Household.Create(userId, time.GetUtcNow(), householdName);
        var (plan, problems) = ImportPlanner.Plan(export, household.Id);
        if (plan is null)
        {
            return new ImportResultResponse { Errors = problems.Select(p => new ImportProblem(p.Path, p.Message)).ToList() };
        }

        // The person who is the importer: the one they chose, or the one the file links to their email.
        var mine = me is { } chosen
            ? plan.PeopleByFileId.GetValueOrDefault(chosen)
            : plan.LinkedEmails.Where(link => IsSameEmail(link.Value, user.Email)).Select(link => link.Key).FirstOrDefault();
        if (me is not null && mine is null)
        {
            return Failed("me", "That person isn't in the file.");
        }

        mine?.LinkTo(userId);

        db.Households.Add(household);
        db.People.AddRange(plan.People);
        db.OutgoingCategories.AddRange(plan.Categories);
        db.IncomeStreams.AddRange(plan.Incomes);
        db.Outgoings.AddRange(plan.Outgoings);
        user.LastActiveHouseholdId = household.Id;

        // One save, so it's all or nothing.
        await db.SaveChangesAsync();

        return new ImportResultResponse { HouseholdId = household.Id, HouseholdName = household.Name };

        static ImportResultResponse Failed(string path, string message) => new() { Errors = [new ImportProblem(path, message)] };
    }

    private static async Task<string?> EmailOfAsync(ClaimsPrincipal principal, BudgetDbContext db)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync();
    }

    private static bool IsSameEmail(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <returns>The request body, or <c>null</c> if it's over the size limit.</returns>
    private static async Task<byte[]?> ReadFileAsync(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > ExportFormat.MaxFileBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>"The Austins' budget" → "the-austins-budget": safe in a file name on any system.</summary>
    public static string FileNamePart(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-' && c is ' ' or '-' or '_' or '.' or '&')
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-');
        return result.Length == 0 ? "household" : result[..Math.Min(result.Length, 50)].TrimEnd('-');
    }
}
