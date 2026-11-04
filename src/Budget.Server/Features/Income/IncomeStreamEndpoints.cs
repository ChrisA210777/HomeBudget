using Budget.Domain.Income;
using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Persistence.Configurations;
using Budget.Server.Features.Households;
using Budget.Shared.Households;
using Budget.Shared.Income;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Income;

/// <summary>
/// Income streams for a household (BUD-30 to BUD-35). Viewers can read; Users and Admins can change.
/// Queries are also filtered to the current household by the database query filter.
/// </summary>
public static class IncomeStreamEndpoints
{
    public static IEndpointRouteBuilder MapIncomeStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var streams = app.MapGroup(HouseholdRoutes.Base + "/income-streams");

        streams.MapGet("/", ListAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        streams.MapGet("/{incomeStreamId:guid}", GetAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        streams.MapPost("/", CreateAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapPut("/{incomeStreamId:guid}", UpdateAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapPost("/{incomeStreamId:guid}/deactivate", DeactivateAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapPost("/{incomeStreamId:guid}/reactivate", ReactivateAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapDelete("/{incomeStreamId:guid}", DeleteAsync).RequireAuthorization(HouseholdPolicies.User);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid householdId, bool? includeInactive, BudgetDbContext db)
    {
        var showInactive = includeInactive == true;
        var streams = await db.IncomeStreams
            .AsNoTracking()
            .Where(s => showInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var memberIds = streams.Select(s => s.MemberUserId).OfType<Guid>().Distinct().ToList();
        var memberNames = await db.Users
            .Where(u => memberIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var currency = await db.Households
            .Where(h => h.Id == householdId)
            .Select(h => h.CurrencyCode)
            .SingleAsync();

        // Totals use full precision and are rounded once at the end. Variable pay counts at its average.
        var totalAnnual = streams.Where(s => s.IsActive).Sum(s => s.Estimate().AverageAnnual);

        return Results.Ok(new IncomeStreamListResponse(
            streams.Select(s => s.ToSummary(s.MemberUserId is { } id ? memberNames.GetValueOrDefault(id) : null)).ToList(),
            PayCalculator.RoundForDisplay(totalAnnual),
            PayCalculator.RoundForDisplay(PayCalculator.PerMonth(totalAnnual)),
            currency));
    }

    private static async Task<IResult> GetAsync(Guid incomeStreamId, BudgetDbContext db)
    {
        var found = await db.IncomeStreams
            .AsNoTracking()
            .OfType<EmploymentIncome>()
            .Where(s => s.Id == incomeStreamId)
            .Select(s => new { Income = s, Version = EF.Property<byte[]>(s, IncomeStreamConfiguration.RowVersion) })
            .SingleOrDefaultAsync();

        return found is null ? Results.NotFound() : Results.Ok(found.Income.ToDetails(found.Version));
    }

    private static async Task<IResult> CreateAsync(Guid householdId, SaveEmploymentIncomeRequest request, BudgetDbContext db)
    {
        var errors = await ValidateAsync(householdId, request, db);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var income = EmploymentIncome.Create(
            householdId,
            request.Name,
            request.EmployerName,
            request.MemberUserId,
            request.PayFrequency.ToDomain(),
            request.ToPay());

        db.IncomeStreams.Add(income);
        await db.SaveChangesAsync();

        return Results.Created(IncomeRoutes.IncomeStream(householdId, income.Id), income.ToDetails(VersionOf(db, income)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid householdId,
        Guid incomeStreamId,
        SaveEmploymentIncomeRequest request,
        BudgetDbContext db)
    {
        var income = await db.IncomeStreams.OfType<EmploymentIncome>().SingleOrDefaultAsync(s => s.Id == incomeStreamId);
        if (income is null)
        {
            return Results.NotFound();
        }

        var errors = await ValidateAsync(householdId, request, db);
        var version = ParseVersion(request.Version);
        if (version is null)
        {
            errors[nameof(request.Version)] = ["Reload the income stream and try again."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Only save if nobody has changed it since this version was loaded.
        db.Entry(income).Property(IncomeStreamConfiguration.RowVersion).OriginalValue = version;
        income.Update(request.Name, request.EmployerName, request.MemberUserId, request.PayFrequency.ToDomain(), request.ToPay());

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ChangedBySomeoneElse();
        }

        return Results.Ok(income.ToDetails(VersionOf(db, income)));
    }

    private static Task<IResult> DeactivateAsync(Guid incomeStreamId, BudgetDbContext db) =>
        ChangeAsync(incomeStreamId, db, income => income.Deactivate());

    private static Task<IResult> ReactivateAsync(Guid incomeStreamId, BudgetDbContext db) =>
        ChangeAsync(incomeStreamId, db, income => income.Reactivate());

    private static async Task<IResult> DeleteAsync(Guid incomeStreamId, BudgetDbContext db)
    {
        var income = await db.IncomeStreams.SingleOrDefaultAsync(s => s.Id == incomeStreamId);
        if (income is null)
        {
            return Results.NotFound();
        }

        db.IncomeStreams.Remove(income);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeAsync(Guid incomeStreamId, BudgetDbContext db, Action<IncomeStream> change)
    {
        var income = await db.IncomeStreams.SingleOrDefaultAsync(s => s.Id == incomeStreamId);
        if (income is null)
        {
            return Results.NotFound();
        }

        change(income);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        Guid householdId,
        SaveEmploymentIncomeRequest request,
        BudgetDbContext db)
    {
        var errors = EmploymentIncomeValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);

        if (request.MemberUserId is { } memberId
            && !await db.HouseholdMembers.AnyAsync(m => m.HouseholdId == householdId && m.UserId == memberId))
        {
            errors[nameof(request.MemberUserId)] = ["Choose someone who is a member of this household."];
        }

        return errors;
    }

    private static byte[]? ParseVersion(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(version);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] VersionOf(BudgetDbContext db, IncomeStream income) =>
        db.Entry(income).Property<byte[]>(IncomeStreamConfiguration.RowVersion).CurrentValue;

    private static IResult ChangedBySomeoneElse() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Changed by someone else",
        detail: "Someone else has changed this income stream since you opened it. Reload to see their changes.");
}
