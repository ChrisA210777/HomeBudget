using System.Globalization;
using Budget.Domain.Common;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Households;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Outgoings;

/// <summary>
/// Bills, savings and allowances (BUD-49 onwards, ADR-011). Viewers can read; Users and Admins can
/// add. Queries are filtered to the current household.
/// </summary>
public static class OutgoingEndpoints
{
    public static IEndpointRouteBuilder MapOutgoingEndpoints(this IEndpointRouteBuilder app)
    {
        var outgoings = app.MapGroup(HouseholdRoutes.Base + "/outgoings");

        outgoings.MapGet("/", GetMonthAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        outgoings.MapPost("/", CreateAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapGet("/paid-from", PaidFromNamesAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        outgoings.MapGet("/payments", PaymentsAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        outgoings.MapGet("/coming-up", ComingUpAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        outgoings.MapGet("/reference-labels", ReferenceLabelsAsync).RequireAuthorization(HouseholdPolicies.Viewer);

        OutgoingDetailEndpoints.Map(outgoings);
        return app;
    }

    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    // ---- Reading ----

    private static async Task<IResult> GetMonthAsync(Guid householdId, string? month, bool? all, int? months, BudgetDbContext db, TimeProvider time)
    {
        if (months is not (null or 1 or 12))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["months"] = ["Ask for 1 month or 12."] });
        }

        var count = months ?? 1;
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
        var last = first.AddMonths(count).AddDays(-1);
        var monthsCovered = Enumerable.Range(0, count).Select(i => first.AddMonths(i)).ToList();

        var outgoings = await db.Outgoings
            .Include(o => o.AmountChanges)
            .Include(o => o.StatusPeriods)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();
        var categories = await db.OutgoingCategories.ToDictionaryAsync(c => c.Id, c => new { c.Name, c.SortOrder, c.Icon });
        var people = await db.People.ToDictionaryAsync(p => p.Id, p => p.DisplayName);
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();

        var items = outgoings
            .Where(o => all == true || (o.StartsOn <= last && !(o.EndsOn <= first)))
            .Select(o =>
            {
                var category = categories.GetValueOrDefault(o.CategoryId);
                var paid = o.AmountChangeOn(last) ?? o.AmountChanges.MinBy(c => c.EffectiveFrom)!;
                // A year is the sum of its 12 monthly budget amounts (ADR-013).
                var exact = monthsCovered.Sum(m => o.MonthlyEquivalentForMonth(m.Year, m.Month));
                return new OutgoingMonthItem(
                    o.Id,
                    o.Name,
                    (OutgoingKindDto)o.Kind,
                    (OutgoingPriorityDto)o.Priority,
                    o.CategoryId,
                    category?.Name ?? string.Empty,
                    category?.SortOrder ?? int.MaxValue,
                    o.PersonId,
                    o.PersonId is { } personId ? people.GetValueOrDefault(personId) : null,
                    paid.Amount,
                    paid.Every,
                    (PaymentUnitDto)paid.Unit,
                    Values.RoundForDisplay(exact),
                    exact,
                    MonthNotes(o, first, last, currency),
                    o.StartsOn,
                    o.EndsOn,
                    o.NextRenewalOnOrAfter(today),
                    o.IsComingUp(today),
                    o.AutoRenews,
                    category?.Icon ?? CategoryIcons.Default,
                    o.PaidFrom);
            })
            .OrderBy(i => i.CategorySortOrder)
            .ThenBy(i => i.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Results.Ok(new OutgoingMonthResponse(year, monthNumber, items, currency, count));
    }

    /// <summary>The payments actually due between two dates (BUD-82, ADR-013), in date order.</summary>
    private static async Task<IResult> PaymentsAsync(Guid householdId, string? from, string? to, BudgetDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        var start = ParseDate(from, "from", errors);
        var end = ParseDate(to, "to", errors);
        if (errors.Count == 0 && (end < start || end.DayNumber - start.DayNumber >= OutgoingRoutes.MaxPaymentDays))
        {
            errors["to"] = [$"Ask for 1 to {OutgoingRoutes.MaxPaymentDays} days, ending on or after the start."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var outgoings = await db.Outgoings
            .Include(o => o.AmountChanges)
            .Include(o => o.StatusPeriods)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();
        var categories = await db.OutgoingCategories.ToDictionaryAsync(c => c.Id, c => new { c.Name, c.SortOrder, c.Icon });
        var people = await db.People.ToDictionaryAsync(p => p.Id, p => p.DisplayName);
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();

        var payments = outgoings
            .SelectMany(o => o.PaymentsBetween(start, end).Select(p =>
            {
                var category = categories.GetValueOrDefault(o.CategoryId);
                return new UpcomingPayment(
                    p.Date,
                    o.Id,
                    o.Name,
                    p.Amount,
                    p.Schedule.Every,
                    (PaymentUnitDto)p.Schedule.Unit,
                    (OutgoingKindDto)o.Kind,
                    (OutgoingPriorityDto)o.Priority,
                    o.CategoryId,
                    category?.Name ?? string.Empty,
                    category?.SortOrder ?? int.MaxValue,
                    category?.Icon ?? CategoryIcons.Default,
                    o.PaidFrom,
                    o.PersonId is { } personId ? people.GetValueOrDefault(personId) : null);
            }))
            .OrderBy(p => p.Date)
            .ThenBy(p => p.CategorySortOrder)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Results.Ok(new UpcomingPaymentsResponse(start, end, currency, payments));
    }

    private static DateOnly ParseDate(string? text, string key, Dictionary<string, string[]> errors)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", Display, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[key] = ["Use the format yyyy-MM-dd, e.g. 2026-11-08."];
        return default;
    }

    /// <summary>Why this month differs from usual: starting, price changes, pauses and ending.</summary>
    internal static List<string> MonthNotes(Outgoing outgoing, DateOnly first, DateOnly last, string currency)
    {
        var notes = new List<(DateOnly Date, string Text)>();
        var changes = outgoing.AmountChanges.OrderBy(c => c.EffectiveFrom).ToList();

        if (outgoing.StartsOn > last)
        {
            notes.Add((outgoing.StartsOn, $"Starts {Day(outgoing.StartsOn)}"));
        }

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            if (change.EffectiveFrom < first || change.EffectiveFrom > last)
            {
                continue;
            }

            if (i == 0)
            {
                notes.Add((change.EffectiveFrom, $"Starts {Day(change.EffectiveFrom)}"));
                continue;
            }

            var previous = changes[i - 1].MonthlyEquivalent;
            var verb = change.MonthlyEquivalent > previous ? "Rises" : change.MonthlyEquivalent < previous ? "Falls" : "Changes";
            var paid = $"{Money.Format(change.Amount, currency)} {OutgoingValidator.DescribeSchedule(change.Every, (PaymentUnitDto)change.Unit)}";
            notes.Add((change.EffectiveFrom, $"{verb} to {paid} from {Day(change.EffectiveFrom)}"));
        }

        foreach (var period in outgoing.StatusPeriods.Where(p => p.Overlaps(first, last.AddDays(1))))
        {
            var text = period.Reason == OutgoingStatusReason.Ended
                ? $"Ended from {Day(period.StartDate)}"
                : period.ResumesOn is { } resumes
                    ? $"Paused from {Day(period.StartDate)} until {Day(resumes)}"
                    : $"Paused from {Day(period.StartDate)} until further notice";
            notes.Add((period.StartDate, text));
        }

        return notes.OrderBy(n => n.Date).Select(n => n.Text).ToList();
    }

    internal static string Day(DateOnly date) => date.ToString("d MMM yyyy", Display);

    /// <summary>Outgoings in their reminder period before renewing, soonest first (BUD-52).</summary>
    private static async Task<IReadOnlyList<RenewalComingUp>> ComingUpAsync(Guid householdId, BudgetDbContext db, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();
        var candidates = await db.Outgoings
            .Where(o => o.RenewalDate != null && o.AlertPeriod != AlertPeriod.None)
            .Include(o => o.AmountChanges)
            .Include(o => o.StatusPeriods)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        return candidates
            .Where(o => o.IsComingUp(today))
            .Select(o =>
            {
                var renewal = o.NextRenewalOnOrAfter(today)!.Value;
                var paid = o.AmountChangeOn(today)!;
                return new RenewalComingUp(
                    o.Id, o.Name, renewal, renewal.DayNumber - today.DayNumber, o.AutoRenews, paid.Amount, paid.Every, (PaymentUnitDto)paid.Unit, currency);
            })
            .OrderBy(r => r.RenewalDate)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Reference names used in the household, most used first (BUD-56).</summary>
    private static async Task<IReadOnlyList<string>> ReferenceLabelsAsync(BudgetDbContext db) =>
        (await db.Outgoings
            .Where(o => o.ReferenceLabel != null)
            .Select(o => o.ReferenceLabel!)
            .ToListAsync())
        .GroupBy(l => l, StringComparer.CurrentCultureIgnoreCase)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
        .Select(g => g.Key)
        .ToList();

    private static async Task<IReadOnlyList<string>> PaidFromNamesAsync(BudgetDbContext db) =>
        (await db.Outgoings
            .Where(o => o.PaidFrom != null)
            .Select(o => o.PaidFrom!)
            .Distinct()
            .ToListAsync())
        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static async Task<IResult> CreateAsync(Guid householdId, CreateOutgoingRequest request, BudgetDbContext db)
    {
        var errors = OutgoingValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);

        if (!errors.ContainsKey(nameof(request.CategoryId))
            && !await db.OutgoingCategories.AnyAsync(c => c.Id == request.CategoryId && !c.IsHidden))
        {
            errors[nameof(request.CategoryId)] = ["Choose a category."];
        }

        if (request.PersonId is { } personId && !await db.People.AnyAsync(p => p.Id == personId))
        {
            errors[nameof(request.PersonId)] = ["Choose someone in this household, or the whole household."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var details = new OutgoingDetails
        {
            Name = request.Name,
            Kind = (OutgoingKind)request.Kind,
            CategoryId = request.CategoryId,
            Priority = (OutgoingPriority)request.Priority!.Value,
            PersonId = request.PersonId,
            FirstPaymentDate = request.FirstPaymentDate!.Value,
            RenewalDate = request.RenewalDate,
            RenewalEveryMonths = request.RenewalDate is null ? 12 : request.RenewalEveryMonths,
            AutoRenews = request.AutoRenews,
            AlertPeriod = (AlertPeriod)request.AlertPeriod,
            Recipient = request.Kind == OutgoingKindDto.Allowance ? null : request.Recipient,
            PaymentMethod = (PaymentMethod?)request.PaymentMethod,
            PaidFrom = request.PaidFrom,
            ReferenceLabel = request.ReferenceLabel,
            ReferenceValue = request.ReferenceValue,
            Website = request.Website,
            ContactPhone = request.ContactPhone,
            Notes = request.Notes,
        };

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? OutgoingValidator.DefaultStartingComment : request.Comment;
        var schedule = new PaymentSchedule(request.Every, (PaymentUnit)request.Unit);
        var outgoing = Outgoing.Create(householdId, details, request.Amount!.Value, schedule, comment);
        db.Outgoings.Add(outgoing);
        await db.SaveChangesAsync();

        return Results.Created(
            OutgoingRoutes.Outgoing(householdId, outgoing.Id),
            new OutgoingCreatedResponse(outgoing.Id, outgoing.Name, Values.RoundForDisplay(schedule.MonthlyEquivalent(request.Amount.Value))));
    }
}
