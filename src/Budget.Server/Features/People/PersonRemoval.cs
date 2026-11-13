using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Activity;
using Budget.Server.Features.Households;
using Budget.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.People;

/// <summary>
/// Admins remove a person who doesn't sign in (BUD-112, ADR-016). Their items are moved or deleted first, as chosen:
/// income to another person or deleted; outgoings to the household or another person, or deleted. Their accounts become
/// the household's, and can be archived too. Invitations to join as them go. Recent activity records the removal
/// without their name, and earlier activity about them loses their name too (BUD-154).
/// </summary>
public static class PersonRemoval
{
    public const string LinkedToMember = "This person signs in as a member. They need to leave, or be removed from Members, first.";

    public static RouteGroupBuilder MapPersonRemoval(this RouteGroupBuilder people)
    {
        people.MapGet("/{personId:guid}/removal", CheckAsync).RequireAuthorization(HouseholdPolicies.Admin);
        people.MapPost("/{personId:guid}/removal", RemoveAsync).RequireAuthorization(HouseholdPolicies.Admin);
        return people;
    }

    private static async Task<IResult> CheckAsync(Guid personId, BudgetDbContext db)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId);
        if (person is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new PersonRemovalCheck(
            await db.IncomeStreams.CountAsync(s => s.PersonId == personId),
            await db.Outgoings.CountAsync(o => o.PersonId == personId),
            await db.Accounts.CountAsync(a => a.OwnerPersonId == personId && a.ParentAccountId == null),
            person.LinkedUserId is not null));
    }

    private static async Task<IResult> RemoveAsync(Guid householdId, Guid personId, RemovePersonRequest request, BudgetDbContext db)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId);
        if (person is null)
        {
            return Results.NotFound();
        }

        if (person.LinkedUserId is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: LinkedToMember);
        }

        var streams = await db.IncomeStreams.Where(s => s.PersonId == personId).ToListAsync();
        var outgoings = await db.Outgoings.Where(o => o.PersonId == personId).ToListAsync();
        var otherPeople = await db.People.Where(p => p.Id != personId).Select(p => p.Id).ToListAsync();

        var errors = new Dictionary<string, string[]>();
        if (streams.Count > 0)
        {
            var valid = request.Income switch
            {
                RemovalChoices.Delete => true,
                RemovalChoices.Person => request.IncomeTo is { } to && otherPeople.Contains(to),
                _ => false,
            };
            if (!valid)
            {
                errors[nameof(request.Income)] = ["Choose another person to move their income to, or delete it."];
            }
        }

        if (outgoings.Count > 0)
        {
            var valid = request.Outgoings switch
            {
                RemovalChoices.Delete or RemovalChoices.Household => true,
                RemovalChoices.Person => request.OutgoingsTo is { } to && otherPeople.Contains(to),
                _ => false,
            };
            if (!valid)
            {
                errors[nameof(request.Outgoings)] = ["Choose what happens to their outgoings."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Invitations change with set-based updates, so everything runs in one transaction.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            foreach (var stream in streams)
            {
                if (request.Income == RemovalChoices.Delete)
                {
                    db.IncomeStreams.Remove(stream);
                    db.ActivityEntries.Add(ActivityMessages.IncomeDeleted(householdId, stream));
                }
                else
                {
                    stream.ChangePerson(request.IncomeTo!.Value);
                }
            }

            foreach (var outgoing in outgoings)
            {
                switch (request.Outgoings)
                {
                    case RemovalChoices.Delete:
                        db.Outgoings.Remove(outgoing);
                        db.ActivityEntries.Add(ActivityMessages.OutgoingDeleted(householdId, outgoing));
                        break;
                    case RemovalChoices.Person:
                        outgoing.AssignTo(request.OutgoingsTo);
                        break;
                    default:
                        outgoing.AssignTo(null);
                        break;
                }
            }

            // Their accounts and pots become the household's.
            foreach (var account in await db.Accounts.Where(a => a.OwnerPersonId == personId).ToListAsync())
            {
                account.FollowOwner(null);
                if (request.ArchiveAccounts)
                {
                    account.Archive();
                }
            }

            // Invitations to join as them go if they're still open; old ones just lose the link.
            await db.Invitations.IgnoreQueryFilters()
                .Where(i => i.PersonId == personId && i.AcceptedAt == null && i.RevokedAt == null)
                .ExecuteDeleteAsync();
            await db.Invitations.IgnoreQueryFilters()
                .Where(i => i.PersonId == personId)
                .ExecuteUpdateAsync(i => i.SetProperty(x => x.PersonId, (Guid?)null));

            // Earlier activity about them keeps what happened, without their name (BUD-154). Summaries are encrypted, so
            // which wording each gets is decided here rather than by the database (BUD-166).
            var earlier = await db.ActivityEntries.IgnoreQueryFilters()
                .Where(a => a.SubjectId == personId)
                .Select(a => new { a.Id, a.Summary })
                .ToListAsync();
            var added = earlier.Where(a => a.Summary.StartsWith("added ", StringComparison.Ordinal)).Select(a => a.Id).ToList();
            var renamed = earlier.Select(a => a.Id).Except(added).ToList();
            await db.ActivityEntries.IgnoreQueryFilters()
                .Where(a => added.Contains(a.Id))
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.Summary, ActivityMessages.RemovedPersonAdded).SetProperty(x => x.SubjectId, (Guid?)null));
            await db.ActivityEntries.IgnoreQueryFilters()
                .Where(a => renamed.Contains(a.Id))
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.Summary, ActivityMessages.RemovedPersonRenamed).SetProperty(x => x.SubjectId, (Guid?)null));

            db.People.Remove(person);
            db.ActivityEntries.Add(ActivityMessages.PersonRemoved(householdId));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return Results.NoContent();
    }
}
