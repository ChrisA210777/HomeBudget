using System.Reflection;
using Budget.Domain.Common;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Shared.DataExport;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Builds a household's export file (BUD-65). The core export is what's needed to carry on: people, categories,
/// and incomes and outgoings that are active or start later, with today's amount, agreed future changes and current
/// or future pauses. History adds everything else; audit adds who recorded what and when. Nothing calculated, and
/// nothing about logins, is ever included.
/// </summary>
public sealed class ExportBuilder(BudgetDbContext db, TimeProvider time)
{
    public static string AppVersion { get; } =
        typeof(ExportBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    /// <remarks>The household query filter must already be set to <paramref name="householdId"/>.</remarks>
    public async Task<HouseholdExport> BuildAsync(Guid householdId, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var household = await db.Households.AsNoTracking().SingleAsync(h => h.Id == householdId);
        var members = await (
                from member in db.HouseholdMembers
                join user in db.Users on member.UserId equals user.Id
                where member.HouseholdId == householdId
                orderby member.IsOwner descending, user.DisplayName
                select new ExportedMember
                {
                    DisplayName = user.DisplayName,
                    Email = user.Email,
                    Role = member.Role.ToString(),
                    IsOwner = member.IsOwner,
                })
            .AsNoTracking()
            .ToListAsync();

        var people = await db.People.AsNoTracking().OrderBy(p => p.DisplayName).ToListAsync();
        var categories = await db.OutgoingCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
        var incomes = await db.IncomeStreams.AsNoTracking()
            .Include(i => i.AmountChanges).Include(i => i.StatusPeriods).Include(i => i.Corrections)
            .AsSplitQuery().OrderBy(i => i.Name).ToListAsync();
        var outgoings = await db.Outgoings.AsNoTracking()
            .Include(o => o.AmountChanges).Include(o => o.StatusPeriods).Include(o => o.Corrections)
            .AsSplitQuery().OrderBy(o => o.Name).ToListAsync();

        // Emails for linked people and, with audit details, names for whoever recorded things. Only users who are
        // linked to this household's people, or who recorded something in it, are looked up.
        var linkedUserIds = people.Where(p => p.LinkedUserId is not null).Select(p => p.LinkedUserId!.Value);
        var auditUserIds = options.IncludeAudit
            ? AllAudited(people, categories, incomes, outgoings).SelectMany(a => new[] { a.CreatedBy, a.ModifiedBy }).OfType<Guid>()
            : [];
        var userIds = linkedUserIds.Concat(auditUserIds).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.DisplayName, u.Email });

        T Audit<T>(T exported, IAuditable source)
            where T : ExportedAudited =>
            options.IncludeAudit
                ? exported with
                {
                    RecordedBy = (source as AuditableEntity)?.OriginallyRecordedBy
                        ?? (source.CreatedBy is { } c ? users.GetValueOrDefault(c)?.DisplayName : null),
                    RecordedAt = (source as AuditableEntity)?.OriginallyRecordedAt ?? source.CreatedAt,
                    ChangedBy = source.ModifiedBy is { } m ? users.GetValueOrDefault(m)?.DisplayName : null,
                    ChangedAt = source.ModifiedAt,
                }
                : exported;

        return new HouseholdExport
        {
            ExportedAt = now,
            AppVersion = AppVersion,
            Options = options,
            Household = new ExportedHousehold { Name = household.Name, CurrencyCode = household.CurrencyCode },
            Members = members,
            People = people.Select(p => Audit(new ExportedPerson
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                LinkedMemberEmail = p.LinkedUserId is { } userId ? users.GetValueOrDefault(userId)?.Email : null,
            }, p)).ToList(),
            Categories = categories.Select(c => Audit(new ExportedCategory
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder,
                IsHidden = c.IsHidden,
                Icon = c.Icon,
            }, c)).ToList(),
            Incomes = incomes
                .Where(i => options.IncludeHistory || !(i.EndsOn <= today))
                .Select(i => Audit(new ExportedIncome
                {
                    Id = i.Id,
                    Name = i.Name,
                    PersonId = i.PersonId,
                    AmountChanges = CurrentAndFuture(i.AmountChanges, c => c.EffectiveFrom, today, options)
                        .Select(c => Audit(AmountChange(c.Id, c.EffectiveFrom, c.Amount, c.Every, c.Unit, c.Comment), c)).ToList(),
                    StatusPeriods = i.StatusPeriods
                        .Where(p => options.IncludeHistory || p.Reason == IncomeStatusReason.Ended || !(p.EndDate < today))
                        .OrderBy(p => p.StartDate)
                        .Select(p => Audit(new ExportedIncomeStatusPeriod
                        {
                            Id = p.Id,
                            Reason = p.Reason.ToString(),
                            StartDate = p.StartDate,
                            EndDate = p.EndDate,
                            ReducedMonthlyAmount = p.ReducedMonthlyAmount,
                            Comment = p.Comment,
                        }, p)).ToList(),
                    Corrections = options.IncludeHistory
                        ? i.Corrections.OrderBy(c => c.RecordedAt).ThenBy(c => c.CreatedAt)
                            .Select(c => Audit(Correction(c.EntryType.ToString(), c.EntryId, c.Action.ToString(), c.Before, c.After, c.Reason), c)).ToList()
                        : [],
                }, i)).ToList(),
            Outgoings = outgoings
                .Where(o => options.IncludeHistory || !(o.EndsOn <= today))
                .Select(o => Audit(new ExportedOutgoing
                {
                    Id = o.Id,
                    Name = o.Name,
                    Kind = o.Kind.ToString(),
                    Priority = o.Priority.ToString(),
                    CategoryId = o.CategoryId,
                    PersonId = o.PersonId,
                    FirstPaymentDate = o.FirstPaymentDate,
                    Renewal = o.RenewalDate is { } renewal
                        ? new ExportedRenewal { Date = renewal, EveryMonths = o.RenewalEveryMonths, AutoRenews = o.AutoRenews, AlertPeriod = o.AlertPeriod.ToString() }
                        : null,
                    Details = new ExportedOutgoingDetails
                    {
                        Recipient = o.Recipient,
                        PaymentMethod = o.PaymentMethod?.ToString(),
                        PaidFrom = o.PaidFrom,
                        ReferenceLabel = o.ReferenceLabel,
                        ReferenceValue = o.ReferenceValue,
                        Website = o.Website,
                        ContactPhone = o.ContactPhone,
                        Notes = o.Notes,
                    },
                    AmountChanges = CurrentAndFuture(o.AmountChanges, c => c.EffectiveFrom, today, options)
                        .Select(c => Audit(AmountChange(c.Id, c.EffectiveFrom, c.Amount, c.Every, c.Unit, c.Comment), c)).ToList(),
                    StatusPeriods = o.StatusPeriods
                        .Where(p => options.IncludeHistory || p.Reason == OutgoingStatusReason.Ended || !(p.ResumesOn <= today))
                        .OrderBy(p => p.StartDate)
                        .Select(p => Audit(new ExportedOutgoingStatusPeriod
                        {
                            Id = p.Id,
                            Reason = p.Reason.ToString(),
                            StartDate = p.StartDate,
                            ResumesOn = p.ResumesOn,
                            Comment = p.Comment,
                        }, p)).ToList(),
                    Corrections = options.IncludeHistory
                        ? o.Corrections.OrderBy(c => c.RecordedAt).ThenBy(c => c.CreatedAt)
                            .Select(c => Audit(Correction(c.EntryType.ToString(), c.EntryId, c.Action.ToString(), c.Before, c.After, c.Reason), c)).ToList()
                        : [],
                }, o)).ToList(),
        };
    }

    /// <summary>
    /// With history, every change. Otherwise the one in force today (it keeps its original date) and any later ones,
    /// or all of them if none has started yet.
    /// </summary>
    private static IEnumerable<T> CurrentAndFuture<T>(IEnumerable<T> changes, Func<T, DateOnly> effectiveFrom, DateOnly today, ExportOptions options)
    {
        var ordered = changes.OrderBy(effectiveFrom).ToList();
        if (options.IncludeHistory)
        {
            return ordered;
        }

        var inForce = ordered.LastOrDefault(c => effectiveFrom(c) <= today);
        return ordered.Where(c => effectiveFrom(c) > today || (inForce is not null && ReferenceEquals(c, inForce)));
    }

    private static ExportedAmountChange AmountChange(Guid id, DateOnly effectiveFrom, decimal amount, int every, PaymentUnit unit, string comment) => new()
    {
        Id = id,
        EffectiveFrom = effectiveFrom,
        Amount = amount,
        Every = every,
        Unit = unit.ToString(),
        Comment = comment,
    };

    private static ExportedCorrection Correction(string entryType, Guid entryId, string action, string before, string? after, string reason) => new()
    {
        EntryType = entryType,
        EntryId = entryId,
        Action = action,
        Before = before,
        After = after,
        Reason = reason,
    };

    private static IEnumerable<IAuditable> AllAudited(
        IEnumerable<IAuditable> people, IEnumerable<IAuditable> categories, IEnumerable<IncomeStream> incomes, IEnumerable<Outgoing> outgoings) =>
        people.Concat(categories)
            .Concat(incomes).Concat(incomes.SelectMany(i => i.AmountChanges.Cast<IAuditable>().Concat(i.StatusPeriods).Concat(i.Corrections)))
            .Concat(outgoings).Concat(outgoings.SelectMany(o => o.AmountChanges.Cast<IAuditable>().Concat(o.StatusPeriods).Concat(o.Corrections)));
}
