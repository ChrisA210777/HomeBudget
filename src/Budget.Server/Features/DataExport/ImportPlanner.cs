using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Budget.Shared.DataExport;

namespace Budget.Server.Features.DataExport;

/// <summary>The household's data built from a file: new entities with new ids, not yet saved.</summary>
public sealed class ImportPlan
{
    public List<Person> People { get; } = [];

    public List<OutgoingCategory> Categories { get; } = [];

    public List<IncomeStream> Incomes { get; } = [];

    public List<Outgoing> Outgoings { get; } = [];

    /// <summary>Each person's <c>linkedMemberEmail</c> in the file, for linking logins when importing.</summary>
    public Dictionary<Person, string> LinkedEmails { get; } = [];
}

/// <summary>
/// Turns a checked file into new domain entities for a household (BUD-67, BUD-63), applying every domain rule
/// (e.g. pauses start on payment dates, periods don't overlap). New ids are made throughout and every reference
/// in the file is remapped. Nothing is saved: a preview uses this as a dry run.
/// </summary>
public static class ImportPlanner
{
    public static (ImportPlan? Plan, IReadOnlyList<ExportProblem> Problems) Plan(HouseholdExport export, Guid householdId)
    {
        ArgumentNullException.ThrowIfNull(export);
        var plan = new ImportPlan();
        var problems = new List<ExportProblem>();

        // Runs one step; a rule it breaks becomes a problem at that place in the file.
        T? Try<T>(string path, Func<T> step)
            where T : class
        {
            try
            {
                return step();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                problems.Add(new ExportProblem(path, Plain(ex)));
                return null;
            }
        }

        var people = new Dictionary<Guid, Guid>();
        for (var i = 0; i < export.People.Count; i++)
        {
            var source = export.People[i];
            var person = Try($"people[{i}]", () => Person.Create(householdId, source.DisplayName));
            if (person is not null)
            {
                people[source.Id] = person.Id;
                plan.People.Add(person);
                if (!string.IsNullOrWhiteSpace(source.LinkedMemberEmail))
                {
                    plan.LinkedEmails[person] = source.LinkedMemberEmail.Trim();
                }
            }
        }

        var categories = new Dictionary<Guid, Guid>();
        for (var i = 0; i < export.Categories.Count; i++)
        {
            var source = export.Categories[i];
            var category = Try($"categories[{i}]", () =>
            {
                var created = OutgoingCategory.Create(householdId, source.Name, source.SortOrder, source.Icon);
                if (source.IsHidden)
                {
                    created.Hide();
                }

                return created;
            });
            if (category is not null)
            {
                categories[source.Id] = category.Id;
                plan.Categories.Add(category);
            }
        }

        for (var i = 0; i < export.Incomes.Count; i++)
        {
            var source = export.Incomes[i];
            var path = $"incomes[{i}]";
            if (!people.TryGetValue(source.PersonId, out var personId) || source.AmountChanges.Count == 0)
            {
                continue;
            }

            var entryIds = new Dictionary<Guid, Guid>();
            var changes = source.AmountChanges.OrderBy(c => c.EffectiveFrom).ToList();
            var first = changes[0];
            var income = Try(path, () => IncomeStream.Create(householdId, personId, source.Name, first.Amount, first.EffectiveFrom, first.Comment, Schedule(first)));
            if (income is null)
            {
                continue;
            }

            entryIds[first.Id] = income.AmountChanges.Single().Id;
            foreach (var change in changes.Skip(1))
            {
                var index = Index(source.AmountChanges, change);
                var added = Try($"{path}.amountChanges[{index}]", () => income.ChangeAmount(change.EffectiveFrom, change.Amount, change.Comment, Schedule(change)));
                if (added is not null)
                {
                    entryIds[change.Id] = added.Id;
                }
            }

            for (var p = 0; p < source.StatusPeriods.Count; p++)
            {
                var period = source.StatusPeriods[p];
                var added = Try($"{path}.statusPeriods[{p}]", () =>
                    income.AddStatusPeriod(Enum.Parse<IncomeStatusReason>(period.Reason), period.StartDate, period.EndDate, period.ReducedMonthlyAmount, period.Comment));
                if (added is not null)
                {
                    entryIds[period.Id] = added.Id;
                }
            }

            for (var c = 0; c < source.Corrections.Count; c++)
            {
                var correction = source.Corrections[c];
                Try($"{path}.corrections[{c}]", () => income.RestoreCorrection(
                    Enum.Parse<IncomeEntryType>(correction.EntryType),
                    EntryId(entryIds, correction.EntryId),
                    Enum.Parse<IncomeCorrectionAction>(correction.Action),
                    correction.Before,
                    correction.After,
                    correction.Reason));
            }

            plan.Incomes.Add(income);
        }

        for (var i = 0; i < export.Outgoings.Count; i++)
        {
            var source = export.Outgoings[i];
            var path = $"outgoings[{i}]";
            if (!categories.TryGetValue(source.CategoryId, out var categoryId) || source.AmountChanges.Count == 0)
            {
                continue;
            }

            Guid? personId = source.PersonId is { } oldPersonId && people.TryGetValue(oldPersonId, out var newPersonId) ? newPersonId : null;
            if (source.PersonId is not null && personId is null)
            {
                continue;
            }

            var details = source.Details ?? new ExportedOutgoingDetails();
            var outgoingDetails = new OutgoingDetails
            {
                Name = source.Name,
                Kind = Enum.Parse<OutgoingKind>(source.Kind),
                Priority = Enum.Parse<OutgoingPriority>(source.Priority),
                CategoryId = categoryId,
                PersonId = personId,
                FirstPaymentDate = source.FirstPaymentDate,
                RenewalDate = source.Renewal?.Date,
                RenewalEveryMonths = source.Renewal?.EveryMonths ?? 12,
                AutoRenews = source.Renewal?.AutoRenews ?? false,
                AlertPeriod = source.Renewal is { } renewal ? Enum.Parse<AlertPeriod>(renewal.AlertPeriod) : AlertPeriod.None,
                Recipient = details.Recipient,
                PaymentMethod = details.PaymentMethod is { } method ? Enum.Parse<PaymentMethod>(method) : null,
                PaidFrom = details.PaidFrom,
                ReferenceLabel = details.ReferenceLabel,
                ReferenceValue = details.ReferenceValue,
                Website = details.Website,
                ContactPhone = details.ContactPhone,
                Notes = details.Notes,
            };

            // Without history, the earliest amount in the file is the one in force today: it may start after the
            // first payment. It's used from the first payment too, since what was paid before isn't in the file.
            var entryIds = new Dictionary<Guid, Guid>();
            var changes = source.AmountChanges.OrderBy(c => c.EffectiveFrom).ToList();
            var first = changes[0];
            if (first.EffectiveFrom < source.FirstPaymentDate)
            {
                problems.Add(new ExportProblem($"{path}.amountChanges[{Index(source.AmountChanges, first)}].effectiveFrom", "is before the first payment."));
                continue;
            }

            var outgoing = Try(path, () => Outgoing.Create(householdId, outgoingDetails, first.Amount, Schedule(first), first.Comment));
            if (outgoing is null)
            {
                continue;
            }

            foreach (var change in changes)
            {
                if (change.EffectiveFrom == source.FirstPaymentDate)
                {
                    entryIds[change.Id] = outgoing.AmountChanges.Single(c => c.EffectiveFrom == source.FirstPaymentDate).Id;
                    continue;
                }

                var added = Try($"{path}.amountChanges[{Index(source.AmountChanges, change)}]", () =>
                    outgoing.ChangeAmount(change.EffectiveFrom, change.Amount, Schedule(change), change.Comment));
                if (added is not null)
                {
                    entryIds[change.Id] = added.Id;
                }
            }

            for (var p = 0; p < source.StatusPeriods.Count; p++)
            {
                var period = source.StatusPeriods[p];
                var added = Try($"{path}.statusPeriods[{p}]", () => period.Reason == nameof(OutgoingStatusReason.Ended)
                    ? outgoing.End(period.StartDate, period.Comment)
                    : outgoing.Pause(period.StartDate, period.ResumesOn, period.Comment));
                if (added is not null)
                {
                    entryIds[period.Id] = added.Id;
                }
            }

            for (var c = 0; c < source.Corrections.Count; c++)
            {
                var correction = source.Corrections[c];
                Try($"{path}.corrections[{c}]", () => outgoing.RestoreCorrection(
                    Enum.Parse<OutgoingEntryType>(correction.EntryType),
                    EntryId(entryIds, correction.EntryId),
                    Enum.Parse<OutgoingCorrectionAction>(correction.Action),
                    correction.Before,
                    correction.After,
                    correction.Reason));
            }

            plan.Outgoings.Add(outgoing);
        }

        return (problems.Count == 0 ? plan : null, problems);
    }

    private static PaymentSchedule Schedule(ExportedAmountChange change) => new(change.Every, Enum.Parse<PaymentUnit>(change.Unit));

    private static int Index(IReadOnlyList<ExportedAmountChange> changes, ExportedAmountChange change)
    {
        for (var i = 0; i < changes.Count; i++)
        {
            if (ReferenceEquals(changes[i], change))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A correction points at the entry's new id, or at a new id of its own if that entry was deleted.</summary>
    private static Guid EntryId(Dictionary<Guid, Guid> entryIds, Guid oldId) =>
        entryIds.TryGetValue(oldId, out var newId) ? newId : Guid.CreateVersion7();

    /// <summary>A domain rule's message, without the parameter name .NET adds to argument exceptions.</summary>
    private static string Plain(Exception ex)
    {
        var message = ex is ArgumentException argument && argument.ParamName is { } name
            ? ex.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal)
            : ex.Message;
        return message.TrimEnd('.') + ".";
    }
}
