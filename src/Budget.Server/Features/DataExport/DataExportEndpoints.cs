using System.Text;
using System.Text.Json;
using Budget.Server.Features.Households;
using Budget.Shared.DataExport;
using Budget.Shared.Households;

namespace Budget.Server.Features.DataExport;

public static class DataExportEndpoints
{
    public static IEndpointRouteBuilder MapDataExportEndpoints(this IEndpointRouteBuilder app)
    {
        var household = app.MapGroup(HouseholdRoutes.Base);

        // Admins only: the file holds everyone's personal and financial details (ADR-012).
        household.MapGet("/export", ExportAsync).RequireAuthorization(HouseholdPolicies.Admin);

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
