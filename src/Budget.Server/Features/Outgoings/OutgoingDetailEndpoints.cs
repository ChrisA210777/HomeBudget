using Budget.Domain.Common;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Persistence.Configurations;
using Budget.Server.Features.Accounts;
using Budget.Server.Features.Households;
using Budget.Shared.Outgoings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Outgoings;

/// <summary>
/// One outgoing's page (BUD-51): details, amount history, pauses, ending and corrections. Every
/// change sends the version that was loaded, so saving over someone else's change gives a 409.
/// </summary>
internal static class OutgoingDetailEndpoints
{
    private const int UpcomingPaymentCount = 12;

    public static void Map(RouteGroupBuilder outgoings)
    {
        outgoings.MapGet("/{outgoingId:guid}", GetAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        outgoings.MapPut("/{outgoingId:guid}", UpdateAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapDelete("/{outgoingId:guid}", DeleteAsync).RequireAuthorization(HouseholdPolicies.User);

        outgoings.MapPost("/{outgoingId:guid}/amount-changes", AddAmountChangeAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapPut("/{outgoingId:guid}/amount-changes/{entryId:guid}", CorrectAmountChangeAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapDelete("/{outgoingId:guid}/amount-changes/{entryId:guid}", DeleteAmountChangeAsync).RequireAuthorization(HouseholdPolicies.User);

        outgoings.MapPost("/{outgoingId:guid}/status-periods", AddStatusPeriodAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapPut("/{outgoingId:guid}/status-periods/{entryId:guid}", CorrectStatusPeriodAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapDelete("/{outgoingId:guid}/status-periods/{entryId:guid}", DeleteStatusPeriodAsync).RequireAuthorization(HouseholdPolicies.User);
    }

    private static async Task<IResult> GetAsync(Guid householdId, Guid outgoingId, BudgetDbContext db, TimeProvider time)
    {
        var outgoing = await Load(db).SingleOrDefaultAsync(o => o.Id == outgoingId);
        return outgoing is null ? Results.NotFound() : Results.Ok(await DetailsAsync(db, householdId, outgoing, time));
    }

    private static async Task<IResult> UpdateAsync(Guid householdId, Guid outgoingId, UpdateOutgoingRequest request, BudgetDbContext db, TimeProvider time)
    {
        var errors = OutgoingValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);
        var currentCategory = await db.Outgoings.Where(o => o.Id == outgoingId).Select(o => (Guid?)o.CategoryId).SingleOrDefaultAsync();
        if (currentCategory is null)
        {
            return Results.NotFound();
        }

        // A hidden category can stay on an outgoing that already uses it, but can't be newly chosen.
        if (!errors.ContainsKey(nameof(request.CategoryId))
            && !await db.OutgoingCategories.AnyAsync(c => c.Id == request.CategoryId && (!c.IsHidden || c.Id == currentCategory)))
        {
            errors[nameof(request.CategoryId)] = ["Choose a category."];
        }

        if (request.PersonId is { } personId && !await db.People.AnyAsync(p => p.Id == personId))
        {
            errors[nameof(request.PersonId)] = ["Choose someone in this household, or the whole household."];
        }

        // A new account name adds the account, or a pot in one (BUD-89). Saved with the change, or not at all.
        var accounts = await AccountDirectory.LoadAsync(db, householdId);
        return await ChangeAsync(db, householdId, outgoingId, request.Version, errors, time, errorKey: nameof(request.RenewalDate), outgoing =>
            outgoing.UpdateDetails(new OutgoingDetails
            {
                Name = request.Name,
                Kind = (OutgoingKind)request.Kind,
                CategoryId = request.CategoryId,
                Priority = (OutgoingPriority)request.Priority!.Value,
                PersonId = request.PersonId,
                FirstPaymentDate = outgoing.FirstPaymentDate,
                RenewalDate = request.RenewalDate,
                RenewalEveryMonths = request.RenewalDate is null ? 12 : request.RenewalEveryMonths,
                AutoRenews = request.AutoRenews,
                AlertPeriod = (AlertPeriod)request.AlertPeriod,
                Recipient = request.Kind == OutgoingKindDto.Allowance ? null : request.Recipient,
                PaymentMethod = (PaymentMethod?)request.PaymentMethod,
                AccountId = accounts.Resolve(request.PaidFrom)?.Id,
                SavingsAccountId = request.Kind == OutgoingKindDto.Savings ? accounts.Resolve(request.Recipient)?.Id : null,
                ReferenceLabel = request.ReferenceLabel,
                ReferenceValue = request.ReferenceValue,
                Website = request.Website,
                ContactPhone = request.ContactPhone,
                Notes = request.Notes,
            }));
    }

    private static async Task<IResult> DeleteAsync(Guid outgoingId, BudgetDbContext db)
    {
        var outgoing = await db.Outgoings.SingleOrDefaultAsync(o => o.Id == outgoingId);
        if (outgoing is null)
        {
            return Results.NotFound();
        }

        db.Outgoings.Remove(outgoing);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---- Amount changes ----

    private static Task<IResult> AddAmountChangeAsync(
        Guid householdId, Guid outgoingId, SaveOutgoingAmountRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, outgoingId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: false)), time,
            errorKey: nameof(request.EffectiveFrom),
            outgoing => outgoing.ChangeAmount(request.EffectiveFrom!.Value, request.Amount!.Value, Schedule(request), request.Comment));

    private static Task<IResult> CorrectAmountChangeAsync(
        Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingAmountRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, outgoingId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: true)), time,
            errorKey: nameof(request.EffectiveFrom),
            outgoing => outgoing.CorrectAmountChange(entryId, request.EffectiveFrom!.Value, request.Amount!.Value, Schedule(request), request.Comment, request.CorrectionReason!));

    private static Task<IResult> DeleteAmountChangeAsync(
        Guid householdId, Guid outgoingId, Guid entryId, [FromBody] DeleteOutgoingEntryRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, outgoingId, request.Version, Errors(OutgoingValidator.Validate(request)), time,
            errorKey: nameof(request.Reason),
            outgoing => outgoing.DeleteAmountChange(entryId, request.Reason));

    // ---- Pauses and ending ----

    private static Task<IResult> AddStatusPeriodAsync(
        Guid householdId, Guid outgoingId, SaveOutgoingStatusRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, outgoingId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: false)), time,
            errorKey: nameof(request.StartDate),
            outgoing =>
            {
                var start = request.StartDate!.Value;
                if (request.Reason == OutgoingStatusReasonDto.Ended)
                {
                    outgoing.End(start, request.Comment);
                }
                else
                {
                    outgoing.Pause(start, ResumesOn(outgoing, request), request.Comment);
                }
            });

    private static Task<IResult> CorrectStatusPeriodAsync(
        Guid householdId, Guid outgoingId, Guid entryId, SaveOutgoingStatusRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, outgoingId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: true)), time,
            errorKey: nameof(request.StartDate),
            outgoing => outgoing.CorrectStatusPeriod(
                entryId,
                (OutgoingStatusReason)request.Reason,
                request.StartDate!.Value,
                request.Reason == OutgoingStatusReasonDto.Ended ? null : ResumesOn(outgoing, request),
                request.Comment,
                request.CorrectionReason!));

    private static Task<IResult> DeleteStatusPeriodAsync(
        Guid householdId, Guid outgoingId, Guid entryId, [FromBody] DeleteOutgoingEntryRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, outgoingId, request.Version, Errors(OutgoingValidator.Validate(request)), time,
            errorKey: nameof(request.Reason),
            outgoing => outgoing.DeleteStatusPeriod(entryId, request.Reason));

    // ---- Helpers ----

    private static PaymentSchedule Schedule(SaveOutgoingAmountRequest request) => new(request.Every, (PaymentUnit)request.Unit);

    private static DateOnly? ResumesOn(Outgoing outgoing, SaveOutgoingStatusRequest request)
    {
        if (request.SkipPayments is not { } payments)
        {
            return null;
        }

        if (!outgoing.IsPaymentDate(request.StartDate!.Value))
        {
            throw new InvalidOperationException("A pause has to start on a payment date.");
        }

        return outgoing.ResumesAfterSkipping(request.StartDate.Value, payments);
    }

    private static Dictionary<string, string[]> Errors(IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => e.Key, e => e.Value);

    /// <summary>
    /// Loads the outgoing, applies <paramref name="change"/> and saves, but only if nobody else has
    /// changed it since <paramref name="version"/> was loaded. A broken rule (e.g. a pause that
    /// doesn't start on a payment date) is reported against <paramref name="errorKey"/>.
    /// </summary>
    private static async Task<IResult> ChangeAsync(
        BudgetDbContext db,
        Guid householdId,
        Guid outgoingId,
        string? version,
        Dictionary<string, string[]> errors,
        TimeProvider time,
        string errorKey,
        Action<Outgoing> change)
    {
        var outgoing = await Load(db).SingleOrDefaultAsync(o => o.Id == outgoingId);
        if (outgoing is null)
        {
            return Results.NotFound();
        }

        var originalVersion = ParseVersion(version);
        if (originalVersion is null)
        {
            errors["Version"] = ["Reload the outgoing and try again."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            change(outgoing);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [errorKey] = [ex.Message] });
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [errorKey] = [ex.Message] });
        }

        // Saving the outgoing itself (even when only its history changed) checks and bumps its version.
        var entry = db.Entry(outgoing);
        entry.Property(OutgoingConfiguration.RowVersion).OriginalValue = originalVersion;
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
                detail: "Someone else has changed this outgoing since you opened it. Reload to see their changes.");
        }

        return Results.Ok(await DetailsAsync(db, householdId, outgoing, time));
    }

    private static IQueryable<Outgoing> Load(BudgetDbContext db) =>
        db.Outgoings.Include(o => o.AmountChanges).Include(o => o.StatusPeriods).Include(o => o.Corrections).AsSplitQuery();

    private static async Task<OutgoingDetailsResponse> DetailsAsync(BudgetDbContext db, Guid householdId, Outgoing outgoing, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var userIds = outgoing.AmountChanges.Select(c => c.CreatedBy)
            .Concat(outgoing.StatusPeriods.Select(p => p.CreatedBy))
            .Concat(outgoing.Corrections.Select(c => c.CreatedBy))
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        string? NameOf(Guid? id) => id is { } userId ? users.GetValueOrDefault(userId) : null;

        var categoryName = await db.OutgoingCategories.Where(c => c.Id == outgoing.CategoryId).Select(c => c.Name).SingleOrDefaultAsync();
        var personName = outgoing.PersonId is { } personId
            ? await db.People.Where(p => p.Id == personId).Select(p => p.DisplayName).SingleOrDefaultAsync()
            : null;
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();
        var version = db.Entry(outgoing).Property<byte[]>(OutgoingConfiguration.RowVersion).CurrentValue;
        var accounts = await AccountDirectory.LoadAsync(db, householdId);

        var current = outgoing.AmountChangeOn(today) ?? outgoing.AmountChanges.MinBy(c => c.EffectiveFrom)!;
        var statusNow = outgoing.StatusPeriods.FirstOrDefault(p => p.Covers(today));
        var status = statusNow switch
        {
            null => null,
            { Reason: OutgoingStatusReason.Ended } => $"Ended from {OutgoingEndpoints.Day(statusNow.StartDate)}",
            { ResumesOn: { } resumes } => $"Paused until {OutgoingEndpoints.Day(resumes)}",
            _ => "Paused until further notice",
        };

        return new OutgoingDetailsResponse(
            outgoing.Id,
            outgoing.Name,
            (OutgoingKindDto)outgoing.Kind,
            (OutgoingPriorityDto)outgoing.Priority,
            outgoing.CategoryId,
            categoryName ?? string.Empty,
            outgoing.PersonId,
            personName,
            current.Amount,
            current.Every,
            (PaymentUnitDto)current.Unit,
            Values.RoundForDisplay(current.MonthlyEquivalent),
            outgoing.FirstPaymentDate,
            outgoing.EndsOn,
            outgoing.NextPaymentOnOrAfter(today),
            outgoing.RenewalDate,
            outgoing.RenewalEveryMonths,
            outgoing.AutoRenews,
            (AlertPeriodDto)outgoing.AlertPeriod,
            outgoing.NextRenewalOnOrAfter(today),
            outgoing.IsComingUp(today),
            status,
            outgoing.Kind == OutgoingKind.Savings ? accounts.DisplayName(outgoing.SavingsAccountId) : outgoing.Recipient,
            (PaymentMethodDto?)outgoing.PaymentMethod,
            accounts.DisplayName(outgoing.AccountId),
            outgoing.ReferenceLabel,
            outgoing.ReferenceValue,
            outgoing.Website,
            outgoing.ContactPhone,
            outgoing.Notes,
            outgoing.EndsOn is { } ends && ends <= today ? [] : outgoing.PaymentDatesFrom(today, UpcomingPaymentCount),
            outgoing.AmountChanges
                .OrderByDescending(c => c.EffectiveFrom)
                .Select(c => new OutgoingAmountEntry(
                    c.Id, c.EffectiveFrom, c.Amount, c.Every, (PaymentUnitDto)c.Unit, Values.RoundForDisplay(c.MonthlyEquivalent), c.Comment,
                    c.RecordedByName ?? NameOf(c.CreatedBy), c.RecordedAt, c.EffectiveFrom > today, c.EffectiveFrom == outgoing.StartsOn))
                .ToList(),
            outgoing.StatusPeriods
                .OrderByDescending(p => p.StartDate)
                .Select(p => new OutgoingStatusEntry(
                    p.Id, (OutgoingStatusReasonDto)p.Reason, p.StartDate, p.ResumesOn, outgoing.PaymentsSkippedBy(p), p.Comment,
                    p.RecordedByName ?? NameOf(p.CreatedBy), p.RecordedAt, p.StartDate > today))
                .ToList(),
            outgoing.Corrections
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new OutgoingCorrectionEntry(c.EntryType.ToString(), c.Action.ToString(), c.Before, c.After, c.Reason, c.RecordedByName ?? NameOf(c.CreatedBy), c.RecordedAt))
                .ToList(),
            currency,
            Convert.ToBase64String(version));
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
}
