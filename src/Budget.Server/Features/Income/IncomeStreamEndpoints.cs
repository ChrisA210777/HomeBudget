using System.Globalization;
using Budget.Domain.Income;
using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Persistence.Configurations;
using Budget.Server.Features.Households;
using Budget.Shared.Households;
using Budget.Shared.Income;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Income;

/// <summary>
/// Income streams as dated timelines (BUD-30 to BUD-35, ADR-009). Viewers can read; Users and Admins
/// can change. Every change to an existing income must send the version that was loaded.
/// </summary>
public static class IncomeStreamEndpoints
{
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    public static IEndpointRouteBuilder MapIncomeStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var streams = app.MapGroup(HouseholdRoutes.Base + "/income-streams");

        streams.MapGet("/", GetMonthAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        streams.MapPost("/", CreateAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapGet("/{incomeStreamId:guid}", GetDetailsAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        streams.MapPut("/{incomeStreamId:guid}", UpdateAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapDelete("/{incomeStreamId:guid}", DeleteAsync).RequireAuthorization(HouseholdPolicies.User);

        streams.MapPost("/{incomeStreamId:guid}/amount-changes", AddAmountChangeAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapPut("/{incomeStreamId:guid}/amount-changes/{entryId:guid}", CorrectAmountChangeAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapDelete("/{incomeStreamId:guid}/amount-changes/{entryId:guid}", DeleteAmountChangeAsync).RequireAuthorization(HouseholdPolicies.User);

        streams.MapPost("/{incomeStreamId:guid}/status-periods", AddStatusPeriodAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapPut("/{incomeStreamId:guid}/status-periods/{entryId:guid}", CorrectStatusPeriodAsync).RequireAuthorization(HouseholdPolicies.User);
        streams.MapDelete("/{incomeStreamId:guid}/status-periods/{entryId:guid}", DeleteStatusPeriodAsync).RequireAuthorization(HouseholdPolicies.User);

        return app;
    }

    // ---- Reading ----

    private static async Task<IResult> GetMonthAsync(Guid householdId, string? month, bool? all, BudgetDbContext db, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        int year, monthNumber;
        if (string.IsNullOrEmpty(month))
        {
            (year, monthNumber) = (today.Year, today.Month);
        }
        else if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", Display, DateTimeStyles.None, out var parsed))
        {
            (year, monthNumber) = (parsed.Year, parsed.Month);
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["month"] = ["Use the format yyyy-MM, e.g. 2026-11."] });
        }

        var first = new DateOnly(year, monthNumber, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var streams = await LoadStreams(db, includeCorrections: false).AsNoTracking().ToListAsync();
        var people = await db.People.ToDictionaryAsync(p => p.Id, p => p.DisplayName);
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();

        var shown = streams
            .Where(s => all == true || (s.StartsOn <= last && !(s.EndsOn <= first)))
            .Select(s => new { Stream = s, Amount = s.AmountForMonth(year, monthNumber) })
            .OrderBy(x => people.GetValueOrDefault(x.Stream.PersonId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Stream.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var items = shown.Select(x => new IncomeMonthItem(
                x.Stream.Id,
                x.Stream.Name,
                x.Stream.PersonId,
                people.GetValueOrDefault(x.Stream.PersonId) ?? string.Empty,
                IncomeTimeline.RoundForDisplay(x.Amount),
                UsualAmount(x.Stream, last),
                Notes(x.Stream, first, last, currency),
                x.Stream.StartsOn,
                x.Stream.EndsOn))
            .ToList();

        // Rounded once, from the unrounded amounts.
        var total = IncomeTimeline.RoundForDisplay(shown.Sum(x => x.Amount));
        return Results.Ok(new IncomeMonthResponse(year, monthNumber, items, total, currency));
    }

    private static async Task<IResult> GetDetailsAsync(Guid householdId, Guid incomeStreamId, BudgetDbContext db, TimeProvider time)
    {
        var stream = await LoadStreams(db, includeCorrections: true).SingleOrDefaultAsync(s => s.Id == incomeStreamId);
        return stream is null ? Results.NotFound() : Results.Ok(await DetailsAsync(db, householdId, stream, time));
    }

    // ---- Creating, renaming, deleting ----

    private static async Task<IResult> CreateAsync(Guid householdId, CreateIncomeStreamRequest request, BudgetDbContext db, TimeProvider time)
    {
        var errors = IncomeValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);
        await CheckPersonAsync(db, request.PersonId, nameof(request.PersonId), errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? IncomeValidator.DefaultStartingComment : request.Comment;
        var stream = IncomeStream.Create(householdId, request.PersonId, request.Name, request.MonthlyNetAmount!.Value, request.StartsOn!.Value, comment);
        db.IncomeStreams.Add(stream);
        await db.SaveChangesAsync();

        return Results.Created(IncomeRoutes.IncomeStream(householdId, stream.Id), await DetailsAsync(db, householdId, stream, time));
    }

    private static async Task<IResult> UpdateAsync(
        Guid householdId, Guid incomeStreamId, UpdateIncomeStreamRequest request, BudgetDbContext db, TimeProvider time)
    {
        var errors = IncomeValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);
        await CheckPersonAsync(db, request.PersonId, nameof(request.PersonId), errors);

        return await ChangeAsync(db, householdId, incomeStreamId, request.Version, errors, time, errorKey: nameof(request.Name), stream =>
        {
            stream.Rename(request.Name);
            stream.ChangePerson(request.PersonId);
        });
    }

    private static async Task<IResult> DeleteAsync(Guid incomeStreamId, BudgetDbContext db)
    {
        var stream = await db.IncomeStreams.SingleOrDefaultAsync(s => s.Id == incomeStreamId);
        if (stream is null)
        {
            return Results.NotFound();
        }

        db.IncomeStreams.Remove(stream);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---- Amount changes ----

    private static Task<IResult> AddAmountChangeAsync(
        Guid householdId, Guid incomeStreamId, SaveAmountChangeRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, incomeStreamId, request.Version, Errors(IncomeValidator.Validate(request, isCorrection: false)), time,
            errorKey: nameof(request.EffectiveFrom),
            stream => stream.ChangeAmount(request.EffectiveFrom!.Value, request.MonthlyNetAmount!.Value, request.Comment));

    private static Task<IResult> CorrectAmountChangeAsync(
        Guid householdId, Guid incomeStreamId, Guid entryId, SaveAmountChangeRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, incomeStreamId, request.Version, Errors(IncomeValidator.Validate(request, isCorrection: true)), time,
            errorKey: nameof(request.EffectiveFrom),
            stream => stream.CorrectAmountChange(entryId, request.EffectiveFrom!.Value, request.MonthlyNetAmount!.Value, request.Comment, request.CorrectionReason!));

    private static Task<IResult> DeleteAmountChangeAsync(
        Guid householdId, Guid incomeStreamId, Guid entryId, [FromBody] DeleteEntryRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, incomeStreamId, request.Version, Errors(IncomeValidator.Validate(request)), time,
            errorKey: nameof(request.Reason),
            stream => stream.DeleteAmountChange(entryId, request.Reason));

    // ---- Status periods ----

    private static Task<IResult> AddStatusPeriodAsync(
        Guid householdId, Guid incomeStreamId, SaveStatusPeriodRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, incomeStreamId, request.Version, Errors(IncomeValidator.Validate(request, isCorrection: false)), time,
            errorKey: nameof(request.StartDate),
            stream => stream.AddStatusPeriod(ToDomain(request.Reason), request.StartDate!.Value, request.EndDate, request.ReducedMonthlyAmount, request.Comment));

    private static Task<IResult> CorrectStatusPeriodAsync(
        Guid householdId, Guid incomeStreamId, Guid entryId, SaveStatusPeriodRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, incomeStreamId, request.Version, Errors(IncomeValidator.Validate(request, isCorrection: true)), time,
            errorKey: nameof(request.StartDate),
            stream => stream.CorrectStatusPeriod(
                entryId, ToDomain(request.Reason), request.StartDate!.Value, request.EndDate, request.ReducedMonthlyAmount, request.Comment, request.CorrectionReason!));

    private static Task<IResult> DeleteStatusPeriodAsync(
        Guid householdId, Guid incomeStreamId, Guid entryId, [FromBody] DeleteEntryRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, incomeStreamId, request.Version, Errors(IncomeValidator.Validate(request)), time,
            errorKey: nameof(request.Reason),
            stream => stream.DeleteStatusPeriod(entryId, request.Reason));

    // ---- Helpers ----

    /// <summary>
    /// Loads the stream, applies <paramref name="change"/> and saves, but only if nobody else has
    /// changed the stream since <paramref name="version"/> was loaded. A broken timeline rule (e.g.
    /// overlapping leave) is reported against <paramref name="errorKey"/>.
    /// </summary>
    private static async Task<IResult> ChangeAsync(
        BudgetDbContext db,
        Guid householdId,
        Guid incomeStreamId,
        string? version,
        Dictionary<string, string[]> errors,
        TimeProvider time,
        string errorKey,
        Action<IncomeStream> change)
    {
        var stream = await LoadStreams(db, includeCorrections: true).SingleOrDefaultAsync(s => s.Id == incomeStreamId);
        if (stream is null)
        {
            return Results.NotFound();
        }

        var originalVersion = ParseVersion(version);
        if (originalVersion is null)
        {
            errors["Version"] = ["Reload the income and try again."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            change(stream);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [errorKey] = [ex.Message] });
        }

        // Saving the stream itself (even when only its timeline changed) checks and bumps its version.
        var entry = db.Entry(stream);
        entry.Property(IncomeStreamConfiguration.RowVersion).OriginalValue = originalVersion;
        entry.State = EntityState.Modified;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Changed by someone else",
                detail: "Someone else has changed this income since you opened it. Reload to see their changes.");
        }

        return Results.Ok(await DetailsAsync(db, householdId, stream, time));
    }

    private static IQueryable<IncomeStream> LoadStreams(BudgetDbContext db, bool includeCorrections)
    {
        var query = db.IncomeStreams.Include(s => s.AmountChanges).Include(s => s.StatusPeriods).AsSplitQuery();
        return includeCorrections ? query.Include(s => s.Corrections) : query;
    }

    private static async Task<IncomeStreamDetails> DetailsAsync(BudgetDbContext db, Guid householdId, IncomeStream stream, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var userIds = stream.AmountChanges.Select(c => c.CreatedBy)
            .Concat(stream.StatusPeriods.Select(p => p.CreatedBy))
            .Concat(stream.Corrections.Select(c => c.CreatedBy))
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        string? NameOf(Guid? id) => id is { } userId ? users.GetValueOrDefault(userId) : null;

        var personName = await db.People.Where(p => p.Id == stream.PersonId).Select(p => p.DisplayName).SingleOrDefaultAsync();
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();
        var version = db.Entry(stream).Property<byte[]>(IncomeStreamConfiguration.RowVersion).CurrentValue;

        return new IncomeStreamDetails(
            stream.Id,
            stream.Name,
            stream.PersonId,
            personName ?? string.Empty,
            IncomeTimeline.RoundForDisplay(stream.AmountOn(today)),
            stream.StartsOn,
            stream.EndsOn,
            stream.AmountChanges
                .OrderByDescending(c => c.EffectiveFrom)
                .Select(c => new AmountChangeEntry(
                    c.Id, c.EffectiveFrom, c.MonthlyNetAmount, c.Comment, NameOf(c.CreatedBy), c.CreatedAt, c.EffectiveFrom > today, c.EffectiveFrom == stream.StartsOn))
                .ToList(),
            stream.StatusPeriods
                .OrderByDescending(p => p.StartDate)
                .Select(p => new StatusPeriodEntry(
                    p.Id, ToDto(p.Reason), p.StartDate, p.EndDate, p.ReducedMonthlyAmount, p.Comment, NameOf(p.CreatedBy), p.CreatedAt, p.StartDate > today))
                .ToList(),
            stream.Corrections
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CorrectionEntry(c.EntryType.ToString(), c.Action.ToString(), c.Before, c.After, c.Reason, NameOf(c.CreatedBy), c.CreatedAt))
                .ToList(),
            currency,
            version is null ? string.Empty : Convert.ToBase64String(version));
    }

    /// <summary>The month's usual amount (its latest amount, ignoring leave), or null before it starts.</summary>
    private static decimal? UsualAmount(IncomeStream stream, DateOnly last) =>
        stream.StartsOn > last ? null : IncomeTimeline.RoundForDisplay(IncomeTimeline.AmountOn(stream.AmountChanges, [], last));

    /// <summary>Why this month differs from usual: starts, amount changes, leave and endings within it.</summary>
    private static List<string> Notes(IncomeStream stream, DateOnly first, DateOnly last, string currency)
    {
        var notes = new List<(DateOnly Date, string Text)>();

        foreach (var change in stream.AmountChanges.Where(c => c.EffectiveFrom >= first && c.EffectiveFrom <= last))
        {
            notes.Add((change.EffectiveFrom, change.EffectiveFrom == stream.StartsOn
                ? $"Starts {Day(change.EffectiveFrom)}"
                : $"Changes to {Money.Format(change.MonthlyNetAmount, currency)} from {Day(change.EffectiveFrom)}"));
        }

        foreach (var period in stream.StatusPeriods.Where(p => p.Overlaps(first, last)))
        {
            var label = IncomeLabels.Reason(ToDto(period.Reason));
            var text = period.Reason == IncomeStatusReason.Ended
                ? $"Ended from {Day(period.StartDate)}"
                : $"{label} {Day(period.StartDate)} – {(period.EndDate is { } end ? Day(end) : "until further notice")}"
                  + (period.ReducedMonthlyAmount is { } reduced ? $" ({Money.Format(reduced, currency)} a month)" : " (no income)");
            notes.Add((period.StartDate, text));
        }

        return notes.OrderBy(n => n.Date).Select(n => n.Text).ToList();
    }

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", Display);

    private static async Task CheckPersonAsync(BudgetDbContext db, Guid personId, string key, Dictionary<string, string[]> errors)
    {
        if (personId != Guid.Empty && !errors.ContainsKey(key) && !await db.People.AnyAsync(p => p.Id == personId))
        {
            errors[key] = ["Choose someone in this household."];
        }
    }

    private static Dictionary<string, string[]> Errors(IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => e.Key, e => e.Value);

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

    public static IncomeStatusReason ToDomain(IncomeStatusReasonDto reason) => Enum.Parse<IncomeStatusReason>(reason.ToString());

    public static IncomeStatusReasonDto ToDto(IncomeStatusReason reason) => Enum.Parse<IncomeStatusReasonDto>(reason.ToString());
}
