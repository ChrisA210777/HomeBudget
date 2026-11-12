using Budget.Domain.Activity;
using Budget.Domain.Common;
using Budget.Domain.Outgoings;
using Budget.Domain.Transfers;
using Budget.Infrastructure.Persistence;
using Budget.Infrastructure.Persistence.Configurations;
using Budget.Server.Features.Accounts;
using Budget.Server.Features.Activity;
using Budget.Server.Features.Households;
using Budget.Server.Features.Outgoings;
using Budget.Shared.Households;
using Budget.Shared.Outgoings;
using Budget.Shared.Transfers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Transfers;

/// <summary>
/// Transfers between accounts (BUD-91, ADR-014). Viewers can read; Users and Admins can change them, but
/// only while the household has "Accounts and transfers" on. Changes send the version that was loaded,
/// so saving over someone else's change gives a 409.
/// </summary>
public static class TransferEndpoints
{
    private const int UpcomingPaymentCount = 12;

    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup(HouseholdRoutes.Base + "/transfers");

        transfers.MapGet("/", ListAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        transfers.MapGet("/{transferId:guid}", GetAsync).RequireAuthorization(HouseholdPolicies.Viewer);

        // Changes need "Accounts and transfers" on, and at least a User.
        static void Change(RouteHandlerBuilder endpoint) =>
            endpoint.AddEndpointFilter(RequireTransfersEnabledAsync).RequireAuthorization(HouseholdPolicies.User);

        Change(transfers.MapPost("/", CreateAsync));
        Change(transfers.MapPut("/{transferId:guid}", UpdateAsync));
        Change(transfers.MapDelete("/{transferId:guid}", DeleteAsync));

        Change(transfers.MapPost("/{transferId:guid}/amount-changes", AddAmountChangeAsync));
        Change(transfers.MapPut("/{transferId:guid}/amount-changes/{entryId:guid}", CorrectAmountChangeAsync));
        Change(transfers.MapDelete("/{transferId:guid}/amount-changes/{entryId:guid}", DeleteAmountChangeAsync));

        Change(transfers.MapPost("/{transferId:guid}/status-periods", AddStatusPeriodAsync));
        Change(transfers.MapPut("/{transferId:guid}/status-periods/{entryId:guid}", CorrectStatusPeriodAsync));
        Change(transfers.MapDelete("/{transferId:guid}/status-periods/{entryId:guid}", DeleteStatusPeriodAsync));

        return app;
    }

    /// <summary>With "Accounts and transfers" off, transfers can be seen but not changed.</summary>
    private static async ValueTask<object?> RequireTransfersEnabledAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var householdId = Guid.TryParse(context.HttpContext.Request.RouteValues["householdId"]?.ToString(), out var parsed) ? parsed : Guid.Empty;
        var db = context.HttpContext.RequestServices.GetRequiredService<BudgetDbContext>();
        if (!await db.Households.AnyAsync(h => h.Id == householdId && h.TransfersEnabled))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Transfers are turned off",
                detail: "An Admin can turn on \"Accounts and transfers\" in the household's settings.");
        }

        return await next(context);
    }

    // ---- Reading ----

    private static async Task<IResult> ListAsync(Guid householdId, BudgetDbContext db, TimeProvider time)
    {
        var today = Today(time);
        var transfers = await Load(db).AsNoTracking().ToListAsync();
        var accounts = await AccountDirectory.LoadAsync(db, householdId);
        var currency = await CurrencyAsync(db, householdId);

        var items = transfers
            .Select(t =>
            {
                var current = t.AmountChangeOn(today) ?? t.AmountChanges.MinBy(c => c.EffectiveFrom)!;
                var top = accounts.Find(t.ToAccountId) is { ParentAccountId: { } parentId } ? accounts.Find(parentId)! : accounts.Find(t.ToAccountId)!;
                return new TransferSummary(
                    t.Id,
                    t.Name,
                    t.FromAccountId,
                    accounts.DisplayName(t.FromAccountId) ?? string.Empty,
                    t.ToAccountId,
                    accounts.DisplayName(t.ToAccountId) ?? string.Empty,
                    top.Id,
                    top.Name,
                    current.Amount,
                    current.Every,
                    (PaymentUnitDto)current.Unit,
                    Values.RoundForDisplay(current.MonthlyEquivalent),
                    t.NextPaymentOnOrAfter(today),
                    Status(t, today),
                    t.EndsOn is { } ends && ends <= today);
            })
            .OrderBy(t => t.ToTopAccountName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.IsEnded)
            .ThenBy(t => t.Name ?? t.FromName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Results.Ok(new TransfersResponse(items, currency));
    }

    private static async Task<IResult> GetAsync(Guid householdId, Guid transferId, BudgetDbContext db, TimeProvider time)
    {
        var transfer = await Load(db).SingleOrDefaultAsync(t => t.Id == transferId);
        return transfer is null ? Results.NotFound() : Results.Ok(await DetailsAsync(db, householdId, transfer, time));
    }

    // ---- Adding, changing and removing ----

    private static async Task<IResult> CreateAsync(Guid householdId, CreateTransferRequest request, BudgetDbContext db, TimeProvider time)
    {
        var errors = TransferValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);
        await CheckAccountsAsync(db, request.FromAccountId, request.ToAccountId, errors, allowArchived: []);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        Transfer transfer;
        try
        {
            transfer = Transfer.Create(
                householdId,
                new TransferDetails
                {
                    Name = request.Name,
                    FromAccountId = request.FromAccountId!.Value,
                    ToAccountId = request.ToAccountId!.Value,
                    FirstPaymentDate = request.FirstPaymentDate!.Value,
                    Notes = request.Notes,
                },
                request.Amount!.Value,
                new PaymentSchedule(request.Every, (PaymentUnit)request.Unit),
                OutgoingValidator.DefaultStartingComment);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ToAccountId)] = [ex.Message] });
        }

        db.Transfers.Add(transfer);
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        db.ActivityEntries.Add(ActivityMessages.TransferAdded(
            householdId, transfer.Id, Label(transfer, directory), request.Amount!.Value, new PaymentSchedule(request.Every, (PaymentUnit)request.Unit)));
        await db.SaveChangesAsync();
        return Results.Created(TransferRoutes.Transfer(householdId, transfer.Id), await DetailsAsync(db, householdId, transfer, time));
    }

    private static async Task<IResult> UpdateAsync(Guid householdId, Guid transferId, UpdateTransferRequest request, BudgetDbContext db, TimeProvider time)
    {
        var errors = TransferValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);
        var current = await db.Transfers.Where(t => t.Id == transferId).Select(t => new { t.FromAccountId, t.ToAccountId }).SingleOrDefaultAsync();
        if (current is null)
        {
            return Results.NotFound();
        }

        // An archived account can stay on a transfer that already uses it, but can't be newly chosen.
        await CheckAccountsAsync(db, request.FromAccountId, request.ToAccountId, errors, allowArchived: [current.FromAccountId, current.ToAccountId]);
        return await ChangeAsync(db, householdId, transferId, request.Version, errors, time, errorKey: nameof(request.ToAccountId), transfer =>
            transfer.UpdateDetails(new TransferDetails
            {
                Name = request.Name,
                FromAccountId = request.FromAccountId!.Value,
                ToAccountId = request.ToAccountId!.Value,
                FirstPaymentDate = transfer.FirstPaymentDate,
                Notes = request.Notes,
            }),
            (transfer, label) => ActivityMessages.TransferUpdated(householdId, transfer.Id, label));
    }

    private static async Task<IResult> DeleteAsync(Guid householdId, Guid transferId, BudgetDbContext db)
    {
        var transfer = await db.Transfers.SingleOrDefaultAsync(t => t.Id == transferId);
        if (transfer is null)
        {
            return Results.NotFound();
        }

        db.Transfers.Remove(transfer);
        db.ActivityEntries.Add(ActivityMessages.TransferDeleted(householdId, transfer.Id, Label(transfer, await AccountDirectory.LoadAsync(db, householdId))));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---- Amount changes ----

    private static Task<IResult> AddAmountChangeAsync(
        Guid householdId, Guid transferId, SaveOutgoingAmountRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, transferId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: false)), time,
            errorKey: nameof(request.EffectiveFrom),
            transfer => transfer.ChangeAmount(request.EffectiveFrom!.Value, request.Amount!.Value, Schedule(request), request.Comment),
            (transfer, label) => ActivityMessages.TransferAmountChanged(householdId, transfer.Id, label, request.Amount!.Value, Schedule(request), request.EffectiveFrom!.Value));

    private static Task<IResult> CorrectAmountChangeAsync(
        Guid householdId, Guid transferId, Guid entryId, SaveOutgoingAmountRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, transferId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: true)), time,
            errorKey: nameof(request.EffectiveFrom),
            transfer => transfer.CorrectAmountChange(entryId, request.EffectiveFrom!.Value, request.Amount!.Value, Schedule(request), request.Comment, request.CorrectionReason!),
            (transfer, label) => ActivityMessages.TransferCorrected(householdId, transfer.Id, label, "an amount", request.CorrectionReason!));

    private static Task<IResult> DeleteAmountChangeAsync(
        Guid householdId, Guid transferId, Guid entryId, [FromBody] DeleteOutgoingEntryRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, transferId, request.Version, Errors(OutgoingValidator.Validate(request)), time,
            errorKey: nameof(request.Reason),
            transfer => transfer.DeleteAmountChange(entryId, request.Reason),
            (transfer, label) => ActivityMessages.TransferEntryDeleted(householdId, transfer.Id, label, "an amount", request.Reason));

    // ---- Pauses and ending ----

    private static Task<IResult> AddStatusPeriodAsync(
        Guid householdId, Guid transferId, SaveOutgoingStatusRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, transferId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: false)), time,
            errorKey: nameof(request.StartDate),
            transfer =>
            {
                var start = request.StartDate!.Value;
                if (request.Reason == OutgoingStatusReasonDto.Ended)
                {
                    transfer.End(start, request.Comment);
                }
                else
                {
                    transfer.Pause(start, ResumesOn(transfer, request), request.Comment);
                }
            },
            (transfer, label) => request.Reason == OutgoingStatusReasonDto.Ended
                ? ActivityMessages.TransferEnded(householdId, transfer.Id, label, request.StartDate!.Value)
                : ActivityMessages.TransferPaused(householdId, transfer.Id, label, request.StartDate!.Value,
                    transfer.StatusPeriods.FirstOrDefault(p => p.Reason == OutgoingStatusReason.Paused && p.StartDate == request.StartDate)?.ResumesOn));

    private static Task<IResult> CorrectStatusPeriodAsync(
        Guid householdId, Guid transferId, Guid entryId, SaveOutgoingStatusRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, transferId, request.Version, Errors(OutgoingValidator.Validate(request, isCorrection: true)), time,
            errorKey: nameof(request.StartDate),
            transfer => transfer.CorrectStatusPeriod(
                entryId,
                (OutgoingStatusReason)request.Reason,
                request.StartDate!.Value,
                request.Reason == OutgoingStatusReasonDto.Ended ? null : ResumesOn(transfer, request),
                request.Comment,
                request.CorrectionReason!),
            (transfer, label) => ActivityMessages.TransferCorrected(
                householdId, transfer.Id, label, request.Reason == OutgoingStatusReasonDto.Ended ? "its end" : "a pause", request.CorrectionReason!));

    private static Task<IResult> DeleteStatusPeriodAsync(
        Guid householdId, Guid transferId, Guid entryId, [FromBody] DeleteOutgoingEntryRequest request, BudgetDbContext db, TimeProvider time) =>
        ChangeAsync(db, householdId, transferId, request.Version, Errors(OutgoingValidator.Validate(request)), time,
            errorKey: nameof(request.Reason),
            transfer => transfer.DeleteStatusPeriod(entryId, request.Reason),
            (transfer, label) => ActivityMessages.TransferEntryDeleted(householdId, transfer.Id, label, "a pause or end", request.Reason));

    // ---- Helpers ----

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>How recent activity names a transfer: its name, or "from Chris current to Joint".</summary>
    private static string Label(Transfer transfer, AccountDirectory accounts) =>
        ActivityMessages.TransferLabel(transfer.Name, accounts.DisplayName(transfer.FromAccountId), accounts.DisplayName(transfer.ToAccountId));

    private static Task<string> CurrencyAsync(BudgetDbContext db, Guid householdId) =>
        db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();

    private static PaymentSchedule Schedule(SaveOutgoingAmountRequest request) => new(request.Every, (PaymentUnit)request.Unit);

    private static DateOnly? ResumesOn(Transfer transfer, SaveOutgoingStatusRequest request)
    {
        if (request.SkipPayments is not { } payments)
        {
            return null;
        }

        if (!transfer.IsPaymentDate(request.StartDate!.Value))
        {
            throw new InvalidOperationException("A pause has to start on a payment date.");
        }

        return transfer.ResumesAfterSkipping(request.StartDate.Value, payments);
    }

    private static string? Status(Transfer transfer, DateOnly today) =>
        transfer.StatusPeriods.FirstOrDefault(p => p.Covers(today)) switch
        {
            null => null,
            { Reason: OutgoingStatusReason.Ended } ended => $"Ended from {OutgoingEndpoints.Day(ended.StartDate)}",
            { ResumesOn: { } resumes } => $"Paused until {OutgoingEndpoints.Day(resumes)}",
            _ => "Paused until further notice",
        };

    /// <summary>Both accounts must be the household's, and not archived unless already in use.</summary>
    private static async Task CheckAccountsAsync(BudgetDbContext db, Guid? from, Guid? to, Dictionary<string, string[]> errors, Guid[] allowArchived)
    {
        foreach (var (key, id) in new[] { (nameof(CreateTransferRequest.FromAccountId), from), (nameof(CreateTransferRequest.ToAccountId), to) })
        {
            if (errors.ContainsKey(key) || id is not { } accountId)
            {
                continue;
            }

            var account = await db.Accounts.Where(a => a.Id == accountId).Select(a => new { a.IsArchived, a.ParentAccountId }).SingleOrDefaultAsync();
            var parentArchived = account?.ParentAccountId is { } parentId && await db.Accounts.AnyAsync(a => a.Id == parentId && a.IsArchived);
            if (account is null || ((account.IsArchived || parentArchived) && !allowArchived.Contains(accountId)))
            {
                errors[key] = ["Choose one of the household's accounts."];
            }
        }
    }

    private static Dictionary<string, string[]> Errors(IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => e.Key, e => e.Value);

    /// <summary>Loads the transfer, applies <paramref name="change"/> and saves, unless someone else changed it since <paramref name="version"/>.</summary>
    private static async Task<IResult> ChangeAsync(
        BudgetDbContext db,
        Guid householdId,
        Guid transferId,
        string? version,
        Dictionary<string, string[]> errors,
        TimeProvider time,
        string errorKey,
        Action<Transfer> change,
        Func<Transfer, string, ActivityEntry> activity)
    {
        var transfer = await Load(db).SingleOrDefaultAsync(t => t.Id == transferId);
        if (transfer is null)
        {
            return Results.NotFound();
        }

        var originalVersion = ParseVersion(version);
        if (originalVersion is null)
        {
            errors["Version"] = ["Reload the transfer and try again."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            change(transfer);
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

        // Recent activity (BUD-139), saved with the change.
        db.ActivityEntries.Add(activity(transfer, Label(transfer, await AccountDirectory.LoadAsync(db, householdId))));

        var entry = db.Entry(transfer);
        entry.Property(TransferConfiguration.RowVersion).OriginalValue = originalVersion;
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
                detail: "Someone else has changed this transfer since you opened it. Reload to see their changes.");
        }

        return Results.Ok(await DetailsAsync(db, householdId, transfer, time));
    }

    private static IQueryable<Transfer> Load(BudgetDbContext db) =>
        db.Transfers.Include(t => t.AmountChanges).Include(t => t.StatusPeriods).Include(t => t.Corrections).AsSplitQuery();

    private static async Task<TransferDetailsResponse> DetailsAsync(BudgetDbContext db, Guid householdId, Transfer transfer, TimeProvider time)
    {
        var today = Today(time);
        var userIds = transfer.AmountChanges.Select(c => c.CreatedBy)
            .Concat(transfer.StatusPeriods.Select(p => p.CreatedBy))
            .Concat(transfer.Corrections.Select(c => c.CreatedBy))
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        string? NameOf(Guid? id) => id is { } userId ? users.GetValueOrDefault(userId) : null;

        var accounts = await AccountDirectory.LoadAsync(db, householdId);
        var version = db.Entry(transfer).Property<byte[]>(TransferConfiguration.RowVersion).CurrentValue;
        var current = transfer.AmountChangeOn(today) ?? transfer.AmountChanges.MinBy(c => c.EffectiveFrom)!;

        return new TransferDetailsResponse(
            transfer.Id,
            transfer.Name,
            transfer.FromAccountId,
            accounts.DisplayName(transfer.FromAccountId) ?? string.Empty,
            transfer.ToAccountId,
            accounts.DisplayName(transfer.ToAccountId) ?? string.Empty,
            current.Amount,
            current.Every,
            (PaymentUnitDto)current.Unit,
            Values.RoundForDisplay(current.MonthlyEquivalent),
            transfer.FirstPaymentDate,
            transfer.EndsOn,
            transfer.NextPaymentOnOrAfter(today),
            Status(transfer, today),
            transfer.Notes,
            transfer.EndsOn is { } ends && ends <= today ? [] : transfer.PaymentDatesFrom(today, UpcomingPaymentCount),
            transfer.AmountChanges
                .OrderByDescending(c => c.EffectiveFrom)
                .Select(c => new OutgoingAmountEntry(
                    c.Id, c.EffectiveFrom, c.Amount, c.Every, (PaymentUnitDto)c.Unit, Values.RoundForDisplay(c.MonthlyEquivalent), c.Comment,
                    c.RecordedByName ?? NameOf(c.CreatedBy), c.RecordedAt, c.EffectiveFrom > today, c.EffectiveFrom == transfer.StartsOn))
                .ToList(),
            transfer.StatusPeriods
                .OrderByDescending(p => p.StartDate)
                .Select(p => new OutgoingStatusEntry(
                    p.Id, (OutgoingStatusReasonDto)p.Reason, p.StartDate, p.ResumesOn, transfer.PaymentsSkippedBy(p), p.Comment,
                    p.RecordedByName ?? NameOf(p.CreatedBy), p.RecordedAt, p.StartDate > today))
                .ToList(),
            transfer.Corrections
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new OutgoingCorrectionEntry(c.EntryType.ToString(), c.Action.ToString(), c.Before, c.After, c.Reason, c.RecordedByName ?? NameOf(c.CreatedBy), c.RecordedAt))
                .ToList(),
            await CurrencyAsync(db, householdId),
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
