using Budget.Domain.Common;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Households;
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

        outgoings.MapPost("/", CreateAsync).RequireAuthorization(HouseholdPolicies.User);
        outgoings.MapGet("/paid-from", PaidFromNamesAsync).RequireAuthorization(HouseholdPolicies.Viewer);

        return app;
    }

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
