using Budget.Domain.Accounts;
using Budget.Domain.Common;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Budget.Domain.Transfers;
using Budget.Server.Features.Accounts;
using Budget.Shared.DataExport;

namespace Budget.Server.Features.DataExport;

/// <summary>The household's data built from a file: new entities with new ids, not yet saved.</summary>
public sealed class ImportPlan
{
    public List<Person> People { get; } = [];

    public List<OutgoingCategory> Categories { get; } = [];

    public List<IncomeStream> Incomes { get; } = [];

    public List<Outgoing> Outgoings { get; } = [];

    /// <summary>Transfers between accounts (version 4, BUD-91).</summary>
    public List<Transfer> Transfers { get; } = [];

    /// <summary>Accounts and pots (BUD-89): from the file's list, plus any named only by an outgoing.</summary>
    public List<Account> Accounts { get; } = [];

    /// <summary>Each person's <c>linkedMemberEmail</c> in the file, for linking logins when importing.</summary>
    public Dictionary<Person, string> LinkedEmails { get; } = [];

    /// <summary>Existing people the file's people were mapped to, when replacing a household's data.</summary>
    public List<Person> KeptPeople { get; } = [];

    /// <summary>The new (or kept) person for each person's id in the file.</summary>
    public Dictionary<Guid, Person> PeopleByFileId { get; } = [];
}

/// <summary>
/// Turns a checked file into new domain entities for a household (BUD-67, BUD-63), applying every domain rule
/// (e.g. pauses start on payment dates, periods don't overlap). New ids are made throughout and every reference
/// in the file is remapped. Nothing is saved: a preview uses this as a dry run.
/// </summary>
public static class ImportPlanner
{
    /// <param name="keepPeople">
    /// When replacing a household's data (BUD-62): people who stay (those linked to members). A person in the file
    /// with the same name, ignoring case, is mapped to them instead of being created.
    /// </param>
    public static (ImportPlan? Plan, IReadOnlyList<ExportProblem> Problems) Plan(HouseholdExport export, Guid householdId, IReadOnlyCollection<Person>? keepPeople = null)
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

        // Who first recorded each item, and when, are kept for information (BUD-63).
        T? Keep<T>(T? entity, ExportedAudited source)
            where T : AuditableEntity
        {
            if (entity is not null && (source.RecordedBy is not null || source.RecordedAt is not null))
            {
                entity.RestoreOriginalRecord(source.RecordedBy, source.RecordedAt);
            }

            return entity;
        }

        var people = new Dictionary<Guid, Guid>();
        for (var i = 0; i < export.People.Count; i++)
        {
            var source = export.People[i];
            var kept = keepPeople?.FirstOrDefault(p => string.Equals(p.DisplayName, source.DisplayName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (kept is not null)
            {
                people[source.Id] = kept.Id;
                plan.PeopleByFileId[source.Id] = kept;
                plan.KeptPeople.Add(kept);
                continue;
            }

            var person = Keep(Try($"people[{i}]", () => Person.Create(householdId, source.DisplayName)), source);
            if (person is not null)
            {
                people[source.Id] = person.Id;
                plan.PeopleByFileId[source.Id] = person;
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
            Keep(category, source);
            if (category is not null)
            {
                categories[source.Id] = category.Id;
                plan.Categories.Add(category);
            }
        }

        // Accounts and pots in the file (version 2); income and outgoings then find theirs by name, and names from older
        // files become accounts and pots the same way existing households' did (BUD-89, ADR-014).
        var accounts = AccountDirectory.InMemory(householdId, plan.Accounts);
        var accountsByIndex = new Dictionary<int, Account>();
        var paymentIds = new Dictionary<Guid, Guid>();
        for (var i = 0; i < export.Accounts.Count; i++)
        {
            var source = export.Accounts[i];
            Guid? ownerId = source.OwnerPersonId is { } oldOwner && people.TryGetValue(oldOwner, out var newOwner) ? newOwner : null;
            var account = Try($"accounts[{i}]", () => Account.Create(householdId, source.Name, ownerId, source.Provider));
            if (account is null)
            {
                continue;
            }

            plan.Accounts.Add(account);
            accountsByIndex[i] = account;
            if (source.Archived)
            {
                account.Archive();
            }

            for (var j = 0; j < source.Pots.Count; j++)
            {
                var potSource = source.Pots[j];
                var pot = Try($"accounts[{i}].pots[{j}]", () => Account.CreatePot(account, potSource.Name));
                if (pot is null)
                {
                    continue;
                }

                plan.Accounts.Add(pot);
                if (potSource.Archived)
                {
                    pot.Archive();
                }
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
            var income = Keep(Try(path, () => IncomeStream.Create(householdId, personId, source.Name, first.Amount, first.EffectiveFrom, first.Comment, Schedule(first))), source);
            if (income is null)
            {
                continue;
            }

            income.PayInto(accounts.Resolve(source.PaidInto, restoreArchived: false)?.Id);
            paymentIds[source.Id] = income.Id;

            entryIds[first.Id] = Keep(income.AmountChanges.Single(), first)!.Id;
            foreach (var change in changes.Skip(1))
            {
                var index = Index(source.AmountChanges, change);
                var added = Keep(Try($"{path}.amountChanges[{index}]", () => income.ChangeAmount(change.EffectiveFrom, change.Amount, change.Comment, Schedule(change))), change);
                if (added is not null)
                {
                    entryIds[change.Id] = added.Id;
                }
            }

            for (var p = 0; p < source.StatusPeriods.Count; p++)
            {
                var period = source.StatusPeriods[p];
                var added = Keep(Try($"{path}.statusPeriods[{p}]", () =>
                    income.AddStatusPeriod(Enum.Parse<IncomeStatusReason>(period.Reason), period.StartDate, period.EndDate, period.ReducedMonthlyAmount, period.Comment)), period);
                if (added is not null)
                {
                    entryIds[period.Id] = added.Id;
                }
            }

            for (var c = 0; c < source.Corrections.Count; c++)
            {
                var correction = source.Corrections[c];
                Keep(Try($"{path}.corrections[{c}]", () => income.RestoreCorrection(
                    Enum.Parse<IncomeEntryType>(correction.EntryType),
                    EntryId(entryIds, correction.EntryId),
                    Enum.Parse<IncomeCorrectionAction>(correction.Action),
                    correction.Before,
                    correction.After,
                    correction.Reason)), correction);
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
                AccountId = accounts.Resolve(details.PaidFrom, restoreArchived: false)?.Id,
                SavingsAccountId = source.Kind == nameof(OutgoingKind.Savings) ? accounts.Resolve(details.Recipient, restoreArchived: false)?.Id : null,
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

            var outgoing = Keep(Try(path, () => Outgoing.Create(householdId, outgoingDetails, first.Amount, Schedule(first), first.Comment)), source);
            if (outgoing is null)
            {
                continue;
            }

            foreach (var change in changes)
            {
                if (change.EffectiveFrom == source.FirstPaymentDate)
                {
                    entryIds[change.Id] = Keep(outgoing.AmountChanges.Single(c => c.EffectiveFrom == source.FirstPaymentDate), change)!.Id;
                    continue;
                }

                var added = Keep(Try($"{path}.amountChanges[{Index(source.AmountChanges, change)}]", () =>
                    outgoing.ChangeAmount(change.EffectiveFrom, change.Amount, Schedule(change), change.Comment)), change);
                if (added is not null)
                {
                    entryIds[change.Id] = added.Id;
                }
            }

            for (var p = 0; p < source.StatusPeriods.Count; p++)
            {
                var period = source.StatusPeriods[p];
                var added = Keep(Try($"{path}.statusPeriods[{p}]", () => period.Reason == nameof(OutgoingStatusReason.Ended)
                    ? outgoing.End(period.StartDate, period.Comment)
                    : outgoing.Pause(period.StartDate, period.ResumesOn, period.Comment)), period);
                if (added is not null)
                {
                    entryIds[period.Id] = added.Id;
                }
            }

            for (var c = 0; c < source.Corrections.Count; c++)
            {
                var correction = source.Corrections[c];
                Keep(Try($"{path}.corrections[{c}]", () => outgoing.RestoreCorrection(
                    Enum.Parse<OutgoingEntryType>(correction.EntryType),
                    EntryId(entryIds, correction.EntryId),
                    Enum.Parse<OutgoingCorrectionAction>(correction.Action),
                    correction.Before,
                    correction.After,
                    correction.Reason)), correction);
            }

            plan.Outgoings.Add(outgoing);
        }

        for (var i = 0; i < export.Transfers.Count; i++)
        {
            var source = export.Transfers[i];
            var path = $"transfers[{i}]";
            if (source.AmountChanges.Count == 0)
            {
                continue;
            }

            // Accounts named only by a transfer are added, as for outgoings and income.
            var from = accounts.Resolve(source.From, restoreArchived: false);
            var to = accounts.Resolve(source.To, restoreArchived: false);
            if (from is null || to is null)
            {
                problems.Add(new ExportProblem($"{path}.{(from is null ? "from" : "to")}", "is required."));
                continue;
            }

            var entryIds = new Dictionary<Guid, Guid>();
            var changes = source.AmountChanges.OrderBy(c => c.EffectiveFrom).ToList();
            var first = changes[0];
            if (first.EffectiveFrom < source.FirstPaymentDate)
            {
                problems.Add(new ExportProblem($"{path}.amountChanges[{Index(source.AmountChanges, first)}].effectiveFrom", "is before the first payment."));
                continue;
            }

            var transfer = Keep(Try(path, () => Transfer.Create(
                householdId,
                new TransferDetails { Name = source.Name, FromAccountId = from.Id, ToAccountId = to.Id, FirstPaymentDate = source.FirstPaymentDate, Notes = source.Notes },
                first.Amount,
                Schedule(first),
                first.Comment)), source);
            if (transfer is null)
            {
                continue;
            }

            foreach (var change in changes)
            {
                if (change.EffectiveFrom == source.FirstPaymentDate)
                {
                    entryIds[change.Id] = Keep(transfer.AmountChanges.Single(c => c.EffectiveFrom == source.FirstPaymentDate), change)!.Id;
                    continue;
                }

                var added = Keep(Try($"{path}.amountChanges[{Index(source.AmountChanges, change)}]", () =>
                    transfer.ChangeAmount(change.EffectiveFrom, change.Amount, Schedule(change), change.Comment)), change);
                if (added is not null)
                {
                    entryIds[change.Id] = added.Id;
                }
            }

            for (var p = 0; p < source.StatusPeriods.Count; p++)
            {
                var period = source.StatusPeriods[p];
                var added = Keep(Try($"{path}.statusPeriods[{p}]", () => period.Reason == nameof(OutgoingStatusReason.Ended)
                    ? transfer.End(period.StartDate, period.Comment)
                    : transfer.Pause(period.StartDate, period.ResumesOn, period.Comment)), period);
                if (added is not null)
                {
                    entryIds[period.Id] = added.Id;
                }
            }

            for (var c = 0; c < source.Corrections.Count; c++)
            {
                var correction = source.Corrections[c];
                Keep(Try($"{path}.corrections[{c}]", () => transfer.RestoreCorrection(
                    Enum.Parse<OutgoingEntryType>(correction.EntryType),
                    EntryId(entryIds, correction.EntryId),
                    Enum.Parse<OutgoingCorrectionAction>(correction.Action),
                    correction.Before,
                    correction.After,
                    correction.Reason)), correction);
            }

            plan.Transfers.Add(transfer);
            paymentIds[source.Id] = transfer.Id;
        }

        // When each account's month starts (version 5, BUD-136): the new id of the income or transfer it starts with.
        foreach (var (index, imported) in accountsByIndex)
        {
            if (export.Accounts[index].MonthStartsWith is { } fileId && paymentIds.TryGetValue(fileId, out var paymentId))
            {
                imported.StartMonthWith(paymentId);
            }
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
