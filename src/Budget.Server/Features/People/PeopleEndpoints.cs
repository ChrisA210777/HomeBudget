using System.Security.Claims;
using Budget.Domain.People;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Households;
using Budget.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.People;

/// <summary>
/// People in a household (BUD-43). Anyone can see them; Users and Admins can add and rename.
/// Queries are filtered to the current household by the database query filter.
/// </summary>
public static class PeopleEndpoints
{
    public const string DuplicateName = "Someone in this household already has that name.";

    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var people = app.MapGroup(HouseholdRoutes.Base + "/people");

        people.MapGet("/", ListAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        people.MapPost("/", AddAsync).RequireAuthorization(HouseholdPolicies.User);
        people.MapPut("/{personId:guid}", RenameAsync).RequireAuthorization(HouseholdPolicies.User);

        return app;
    }

    /// <summary>People in name order, with the caller's own person first.</summary>
    private static async Task<IReadOnlyList<PersonSummary>> ListAsync(ClaimsPrincipal principal, BudgetDbContext db)
    {
        var callerId = CallerId(principal);

        var people = await (
                from person in db.People
                join user in db.Users on person.LinkedUserId equals (Guid?)user.Id into linked
                from user in linked.DefaultIfEmpty()
                select new { person.Id, person.DisplayName, person.LinkedUserId, LinkedUserName = user == null ? null : user.DisplayName })
            .AsNoTracking()
            .ToListAsync();

        return people
            .Select(p => new PersonSummary(p.Id, p.DisplayName, p.LinkedUserId, p.LinkedUserName, p.LinkedUserId == callerId))
            .OrderByDescending(p => p.IsYou)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task<IResult> AddAsync(Guid householdId, SavePersonRequest request, BudgetDbContext db)
    {
        var errors = await ValidateAsync(request, db, exceptPersonId: null);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var person = Person.Create(householdId, request.DisplayName);
        db.People.Add(person);
        await db.SaveChangesAsync();

        return Results.Created(
            PeopleRoutes.Person(householdId, person.Id),
            new PersonSummary(person.Id, person.DisplayName, null, null, IsYou: false));
    }

    private static async Task<IResult> RenameAsync(
        Guid personId,
        SavePersonRequest request,
        ClaimsPrincipal principal,
        BudgetDbContext db)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId);
        if (person is null)
        {
            return Results.NotFound();
        }

        var errors = await ValidateAsync(request, db, exceptPersonId: personId);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        person.Rename(request.DisplayName);
        await db.SaveChangesAsync();

        var linkedUserName = person.LinkedUserId is { } linkedId
            ? await db.Users.Where(u => u.Id == linkedId).Select(u => u.DisplayName).SingleOrDefaultAsync()
            : null;

        return Results.Ok(new PersonSummary(person.Id, person.DisplayName, person.LinkedUserId, linkedUserName, person.LinkedUserId == CallerId(principal)));
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(SavePersonRequest request, BudgetDbContext db, Guid? exceptPersonId)
    {
        var errors = PersonValidator.Validate(request).ToDictionary(e => e.Key, e => e.Value);
        if (errors.Count > 0)
        {
            return errors;
        }

        // The database comparison is case-insensitive, matching the unique index.
        var name = request.DisplayName.Trim();
        if (await db.People.AnyAsync(p => p.DisplayName == name && p.Id != exceptPersonId))
        {
            errors[nameof(SavePersonRequest.DisplayName)] = [DuplicateName];
        }

        return errors;
    }

    private static Guid? CallerId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
