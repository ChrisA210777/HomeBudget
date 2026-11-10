using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Domain.Transfers;
using Budget.Domain.People;
using Budget.Shared.DataExport;
using Budget.Shared.Outgoings;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Checks a file's contents before anything is imported (BUD-64): required values, lengths, known names for enum
/// values, and that every reference points at something in the file. Each problem says where it is.
/// The domain's own rules (e.g. pauses that start on a payment date) are applied again when importing.
/// </summary>
public static class ExportValidator
{
    public static IReadOnlyList<ExportProblem> Validate(HouseholdExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        var problems = new List<ExportProblem>();
        void Add(string path, string message) => problems.Add(new ExportProblem(path, message));

        void Text(string path, string? value, int max, bool required = true)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    Add(path, "is required.");
                }
            }
            else if (value.Trim().Length > max)
            {
                Add(path, $"can be at most {max} characters.");
            }
        }

        void OneOf(string path, string? value, IReadOnlyList<string> allowed, bool required = true)
        {
            if (value is null)
            {
                if (required)
                {
                    Add(path, $"is required: one of {string.Join(", ", allowed)}.");
                }
            }
            else if (!allowed.Contains(value, StringComparer.Ordinal))
            {
                Add(path, $"\"{value}\" isn't one of {string.Join(", ", allowed)}.");
            }
        }

        void UniqueIds(string section, IEnumerable<Guid> ids)
        {
            var seen = new HashSet<Guid>();
            var index = 0;
            foreach (var id in ids)
            {
                if (id == Guid.Empty)
                {
                    Add($"{section}[{index}].id", "is required.");
                }
                else if (!seen.Add(id))
                {
                    Add($"{section}[{index}].id", "is used more than once.");
                }

                index++;
            }
        }

        Text("household.name", export.Household?.Name, Household.NameMaxLength);
        if (export.Household?.CurrencyCode is not { Length: 3 } currency || !currency.All(char.IsAsciiLetterUpper))
        {
            Add("household.currencyCode", "must be a three-letter currency code, e.g. GBP.");
        }

        for (var i = 0; i < export.Members.Count; i++)
        {
            Text($"members[{i}].displayName", export.Members[i].DisplayName, 200);
            OneOf($"members[{i}].role", export.Members[i].Role, ExportValues.Roles);
        }

        UniqueIds("people", export.People.Select(p => p.Id));
        var personNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < export.People.Count; i++)
        {
            var name = export.People[i].DisplayName;
            Text($"people[{i}].displayName", name, Person.DisplayNameMaxLength);
            if (!string.IsNullOrWhiteSpace(name) && !personNames.Add(name.Trim()))
            {
                Add($"people[{i}].displayName", $"\"{name}\" is used by more than one person.");
            }
        }

        UniqueIds("categories", export.Categories.Select(c => c.Id));
        var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < export.Categories.Count; i++)
        {
            var category = export.Categories[i];
            Text($"categories[{i}].name", category.Name, OutgoingCategory.NameMaxLength);
            if (!string.IsNullOrWhiteSpace(category.Name) && !categoryNames.Add(category.Name.Trim()))
            {
                Add($"categories[{i}].name", $"\"{category.Name}\" is used by more than one category.");
            }

            if (!CategoryIcons.IsKnown(category.Icon))
            {
                Add($"categories[{i}].icon", $"\"{category.Icon}\" isn't one of the category icons.");
            }
        }

        var peopleIds = export.People.Select(p => p.Id).ToHashSet();
        var categoryIds = export.Categories.Select(c => c.Id).ToHashSet();

        // Accounts and their pots (version 2, BUD-89): names unique among accounts, and among an account's pots.
        var accountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < export.Accounts.Count; i++)
        {
            var account = export.Accounts[i];
            Text($"accounts[{i}].name", account.Name, Account.NameMaxLength);
            Text($"accounts[{i}].provider", account.Provider, Account.ProviderMaxLength, required: false);
            if (account.OwnerPersonId is { } owner && !peopleIds.Contains(owner))
            {
                Add($"accounts[{i}].ownerPersonId", "isn't a person in this file.");
            }

            if (!string.IsNullOrWhiteSpace(account.Name) && !accountNames.Add(account.Name.Trim()))
            {
                Add($"accounts[{i}].name", $"\"{account.Name}\" is used by more than one account.");
            }

            var potNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < account.Pots.Count; j++)
            {
                var pot = account.Pots[j];
                Text($"accounts[{i}].pots[{j}].name", pot.Name, Account.NameMaxLength);
                if (!string.IsNullOrWhiteSpace(pot.Name) && !potNames.Add(pot.Name.Trim()))
                {
                    Add($"accounts[{i}].pots[{j}].name", $"\"{pot.Name}\" is used by more than one pot in {account.Name}.");
                }
            }
        }


        UniqueIds("incomes", export.Incomes.Select(i => i.Id));
        for (var i = 0; i < export.Incomes.Count; i++)
        {
            var income = export.Incomes[i];
            var path = $"incomes[{i}]";
            Text($"{path}.paidInto", export.Incomes[i].PaidInto, OutgoingValidator.PaidFromMaxLength, required: false);
            Text($"{path}.name", income.Name, IncomeStream.NameMaxLength);
            if (!peopleIds.Contains(income.PersonId))
            {
                Add($"{path}.personId", "doesn't match any of the people in the file.");
            }

            AmountChanges(path, income.AmountChanges, IncomeStream.CommentMaxLength);
            for (var p = 0; p < income.StatusPeriods.Count; p++)
            {
                var period = income.StatusPeriods[p];
                var periodPath = $"{path}.statusPeriods[{p}]";
                OneOf($"{periodPath}.reason", period.Reason, ExportValues.IncomeStatusReasons);
                if (period.EndDate < period.StartDate)
                {
                    Add($"{periodPath}.endDate", "is before the start date.");
                }

                if (period.ReducedMonthlyAmount < 0)
                {
                    Add($"{periodPath}.reducedMonthlyAmount", "can't be less than 0.");
                }

                Text($"{periodPath}.comment", period.Comment, IncomeStream.CommentMaxLength);
            }

            UniqueIds($"{path}.statusPeriods", income.StatusPeriods.Select(p => p.Id));
            Corrections(path, income.Corrections, IncomeCorrection.DescriptionMaxLength);
        }

        UniqueIds("outgoings", export.Outgoings.Select(o => o.Id));
        for (var i = 0; i < export.Outgoings.Count; i++)
        {
            var outgoing = export.Outgoings[i];
            var path = $"outgoings[{i}]";
            Text($"{path}.name", outgoing.Name, Outgoing.NameMaxLength);
            OneOf($"{path}.kind", outgoing.Kind, ExportValues.Kinds);
            OneOf($"{path}.priority", outgoing.Priority, ExportValues.Priorities);
            if (!categoryIds.Contains(outgoing.CategoryId))
            {
                Add($"{path}.categoryId", "doesn't match any of the categories in the file.");
            }

            if (outgoing.PersonId is { } personId && !peopleIds.Contains(personId))
            {
                Add($"{path}.personId", "doesn't match any of the people in the file.");
            }

            if (outgoing.FirstPaymentDate == default)
            {
                Add($"{path}.firstPaymentDate", "is required.");
            }

            if (outgoing.Renewal is { } renewal)
            {
                if (renewal.EveryMonths is < 1 or > 120)
                {
                    Add($"{path}.renewal.everyMonths", "must be between 1 and 120.");
                }

                OneOf($"{path}.renewal.alertPeriod", renewal.AlertPeriod, ExportValues.AlertPeriods);
            }

            var details = outgoing.Details ?? new ExportedOutgoingDetails();
            Text($"{path}.details.recipient", details.Recipient, Outgoing.RecipientMaxLength, required: false);
            OneOf($"{path}.details.paymentMethod", details.PaymentMethod, ExportValues.PaymentMethods, required: false);
            Text($"{path}.details.paidFrom", details.PaidFrom, OutgoingValidator.PaidFromMaxLength, required: false);
            Text($"{path}.details.referenceLabel", details.ReferenceLabel, Outgoing.ReferenceLabelMaxLength, required: false);
            Text($"{path}.details.referenceValue", details.ReferenceValue, Outgoing.ReferenceValueMaxLength, required: false);
            Text($"{path}.details.website", details.Website, Outgoing.WebsiteMaxLength, required: false);
            Text($"{path}.details.contactPhone", details.ContactPhone, Outgoing.ContactPhoneMaxLength, required: false);
            Text($"{path}.details.notes", details.Notes, Outgoing.NotesMaxLength, required: false);

            AmountChanges(path, outgoing.AmountChanges, Outgoing.CommentMaxLength);
            for (var p = 0; p < outgoing.StatusPeriods.Count; p++)
            {
                var period = outgoing.StatusPeriods[p];
                var periodPath = $"{path}.statusPeriods[{p}]";
                OneOf($"{periodPath}.reason", period.Reason, ExportValues.OutgoingStatusReasons);
                if (period.ResumesOn <= period.StartDate)
                {
                    Add($"{periodPath}.resumesOn", "must be after the start date.");
                }

                if (period.Reason == "Ended" && period.ResumesOn is not null)
                {
                    Add($"{periodPath}.resumesOn", "must be empty for an ending.");
                }

                Text($"{periodPath}.comment", period.Comment, Outgoing.CommentMaxLength);
            }

            UniqueIds($"{path}.statusPeriods", outgoing.StatusPeriods.Select(p => p.Id));
            Corrections(path, outgoing.Corrections, OutgoingCorrection.DescriptionMaxLength);
        }

        // Transfers between accounts (version 4, BUD-91).
        UniqueIds("transfers", export.Transfers.Select(t => t.Id));
        for (var i = 0; i < export.Transfers.Count; i++)
        {
            var transfer = export.Transfers[i];
            var path = $"transfers[{i}]";
            Text($"{path}.name", transfer.Name, Transfer.NameMaxLength, required: false);
            Text($"{path}.from", transfer.From, OutgoingValidator.PaidFromMaxLength);
            Text($"{path}.to", transfer.To, OutgoingValidator.PaidFromMaxLength);
            Text($"{path}.notes", transfer.Notes, Transfer.NotesMaxLength, required: false);
            if (transfer.FirstPaymentDate == default)
            {
                Add($"{path}.firstPaymentDate", "is required.");
            }

            AmountChanges(path, transfer.AmountChanges, Transfer.CommentMaxLength);
            for (var p = 0; p < transfer.StatusPeriods.Count; p++)
            {
                var period = transfer.StatusPeriods[p];
                var periodPath = $"{path}.statusPeriods[{p}]";
                OneOf($"{periodPath}.reason", period.Reason, ExportValues.OutgoingStatusReasons);
                if (period.ResumesOn <= period.StartDate)
                {
                    Add($"{periodPath}.resumesOn", "must be after the start date.");
                }

                if (period.Reason == "Ended" && period.ResumesOn is not null)
                {
                    Add($"{periodPath}.resumesOn", "must be empty for an ending.");
                }

                Text($"{periodPath}.comment", period.Comment, Transfer.CommentMaxLength);
            }

            UniqueIds($"{path}.statusPeriods", transfer.StatusPeriods.Select(p => p.Id));
            Corrections(path, transfer.Corrections, TransferCorrection.DescriptionMaxLength);
        }

        return problems;

        void AmountChanges(string parent, IReadOnlyList<ExportedAmountChange> changes, int commentMax)
        {
            if (changes.Count == 0)
            {
                Add($"{parent}.amountChanges", "needs at least one amount.");
            }

            UniqueIds($"{parent}.amountChanges", changes.Select(c => c.Id));
            var dates = new HashSet<DateOnly>();
            for (var c = 0; c < changes.Count; c++)
            {
                var change = changes[c];
                var path = $"{parent}.amountChanges[{c}]";
                if (change.EffectiveFrom == default)
                {
                    Add($"{path}.effectiveFrom", "is required.");
                }
                else if (!dates.Add(change.EffectiveFrom))
                {
                    Add($"{path}.effectiveFrom", "has another amount on the same date.");
                }

                if (change.Amount <= 0)
                {
                    Add($"{path}.amount", "must be more than 0.");
                }
                else if (decimal.Round(change.Amount, 2) != change.Amount)
                {
                    Add($"{path}.amount", "can have at most 2 decimal places.");
                }

                if (change.Every is < 1 or > PaymentSchedule.MaxEvery)
                {
                    Add($"{path}.every", $"must be between 1 and {PaymentSchedule.MaxEvery}.");
                }

                OneOf($"{path}.unit", change.Unit, ExportValues.Units);
                Text($"{path}.comment", change.Comment, commentMax);
            }
        }

        void Corrections(string parent, IReadOnlyList<ExportedCorrection> corrections, int textMax)
        {
            for (var c = 0; c < corrections.Count; c++)
            {
                var correction = corrections[c];
                var path = $"{parent}.corrections[{c}]";
                OneOf($"{path}.entryType", correction.EntryType, ExportValues.EntryTypes);
                OneOf($"{path}.action", correction.Action, ExportValues.CorrectionActions);
                Text($"{path}.before", correction.Before, textMax);
                Text($"{path}.after", correction.After, textMax, required: correction.Action == "Edited");
                Text($"{path}.reason", correction.Reason, Outgoing.CommentMaxLength);
            }
        }
    }
}
