using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Shared.Households;
using Budget.Shared.Outgoings;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Outgoings;

/// <summary>
/// A household's outgoing categories (BUD-47). Anyone can see them; Users and Admins can add,
/// rename, hide, show and delete unused ones. Queries are filtered to the current household.
/// </summary>
public static class OutgoingCategoryEndpoints
{
    public const string DuplicateName = "There's already a category with that name.";

    public static IEndpointRouteBuilder MapOutgoingCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup(HouseholdRoutes.Base + "/outgoing-categories");

        categories.MapGet("/", ListAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        categories.MapPost("/", AddAsync).RequireAuthorization(HouseholdPolicies.User);
        categories.MapPut("/{categoryId:guid}", UpdateAsync).RequireAuthorization(HouseholdPolicies.User);
        categories.MapDelete("/{categoryId:guid}", DeleteAsync).RequireAuthorization(HouseholdPolicies.User);

        return app;
    }

    private static async Task<IReadOnlyList<OutgoingCategorySummary>> ListAsync(BudgetDbContext db) =>
        await db.OutgoingCategories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new OutgoingCategorySummary(c.Id, c.Name, c.IsHidden, db.Outgoings.Count(o => o.CategoryId == c.Id), c.Icon))
            .AsNoTracking()
            .ToListAsync();

    private static async Task<IResult> AddAsync(Guid householdId, AddCategoryRequest request, BudgetDbContext db)
    {
        var errors = Merge(await ValidateNameAsync(request.Name, db, exceptId: null), CategoryValidator.ValidateIcon(request.Icon));
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var last = await db.OutgoingCategories.MaxAsync(c => (int?)c.SortOrder) ?? 0;
        var category = OutgoingCategory.Create(householdId, request.Name, last + 10, request.Icon);
        db.OutgoingCategories.Add(category);
        await db.SaveChangesAsync();

        return Results.Created(
            OutgoingCategoryRoutes.Category(householdId, category.Id),
            new OutgoingCategorySummary(category.Id, category.Name, category.IsHidden, 0, category.Icon));
    }

    private static async Task<IResult> UpdateAsync(Guid categoryId, UpdateCategoryRequest request, BudgetDbContext db)
    {
        var category = await db.OutgoingCategories.SingleOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
        {
            return Results.NotFound();
        }

        var errors = Merge(await ValidateNameAsync(request.Name, db, exceptId: categoryId), CategoryValidator.ValidateIcon(request.Icon));
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        category.Rename(request.Name);
        if (request.Icon is not null)
        {
            category.ChangeIcon(request.Icon);
        }

        if (request.IsHidden)
        {
            category.Hide();
        }
        else
        {
            category.Show();
        }

        await db.SaveChangesAsync();

        var count = await db.Outgoings.CountAsync(o => o.CategoryId == categoryId);
        return Results.Ok(new OutgoingCategorySummary(category.Id, category.Name, category.IsHidden, count, category.Icon));
    }

    private static Dictionary<string, string[]> Merge(params IReadOnlyDictionary<string, string[]>[] errors) =>
        errors.SelectMany(e => e).ToDictionary(e => e.Key, e => e.Value);

    private static async Task<IResult> DeleteAsync(Guid categoryId, BudgetDbContext db)
    {
        var category = await db.OutgoingCategories.SingleOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
        {
            return Results.NotFound();
        }

        var inUse = await db.Outgoings.CountAsync(o => o.CategoryId == categoryId);
        if (inUse > 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This category is in use.",
                detail: $"{inUse} outgoing{(inUse == 1 ? " uses" : "s use")} it. Hide it instead, or move them to another category first.");
        }

        db.OutgoingCategories.Remove(category);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<Dictionary<string, string[]>> ValidateNameAsync(string? name, BudgetDbContext db, Guid? exceptId)
    {
        var errors = CategoryValidator.ValidateName(name).ToDictionary(e => e.Key, e => e.Value);
        if (errors.Count > 0)
        {
            return errors;
        }

        // The database comparison is case-insensitive, matching the unique index.
        var trimmed = name!.Trim();
        if (await db.OutgoingCategories.AnyAsync(c => c.Name == trimmed && c.Id != exceptId))
        {
            errors["Name"] = [DuplicateName];
        }

        return errors;
    }
}
