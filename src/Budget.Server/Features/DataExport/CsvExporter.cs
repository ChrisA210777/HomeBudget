using System.Globalization;
using System.IO.Compression;
using System.Text;
using Budget.Shared.DataExport;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Writes an export as a zip of CSV files for Excel (BUD-66): one raw table per file, joined by id columns, with no
/// calculated columns. UTF-8 with a byte-order mark so £ and accents open correctly, ISO dates, plain decimals, and
/// cells that could run as a formula made safe. Export only: imports use the JSON file.
/// </summary>
public static class CsvExporter
{
    private static readonly string[] AuditColumns = ["recordedBy", "recordedAt", "changedBy", "changedAt"];

    public static byte[] Write(HouseholdExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        var audit = export.Options.IncludeAudit;

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Table<T>(string name, string[] columns, IEnumerable<T> rows, Func<T, object?[]> values, Func<T, ExportedAudited?>? audited = null)
            {
                var header = audit && audited is not null ? columns.Concat(AuditColumns).ToArray() : columns;
                var lines = rows.Select(row =>
                {
                    var cells = values(row);
                    if (audit && audited is not null)
                    {
                        var a = audited(row);
                        cells = [.. cells, a?.RecordedBy, a?.RecordedAt, a?.ChangedBy, a?.ChangedAt];
                    }

                    return cells;
                });
                Add(archive, name, header, lines);
            }

            Table("household.csv", ["name", "currencyCode", "exportedAt", "schemaVersion"], [export],
                e => [e.Household.Name, e.Household.CurrencyCode, e.ExportedAt, e.SchemaVersion]);
            Table("members.csv", ["displayName", "email", "role", "isOwner"], export.Members,
                m => [m.DisplayName, m.Email, m.Role, m.IsOwner]);
            Table("people.csv", ["id", "displayName", "linkedMemberEmail"], export.People,
                p => [p.Id, p.DisplayName, p.LinkedMemberEmail], p => p);
            Table("categories.csv", ["id", "name", "sortOrder", "isHidden", "icon"], export.Categories,
                c => [c.Id, c.Name, c.SortOrder, c.IsHidden, c.Icon], c => c);

            Table("incomes.csv", ["id", "name", "personId"], export.Incomes,
                i => [i.Id, i.Name, i.PersonId], i => i);
            Table("income_amount_changes.csv", ["id", "incomeId", "effectiveFrom", "amount", "every", "unit", "comment"],
                export.Incomes.SelectMany(i => i.AmountChanges.Select(c => (Parent: i.Id, Change: c))),
                x => [x.Change.Id, x.Parent, x.Change.EffectiveFrom, x.Change.Amount, x.Change.Every, x.Change.Unit, x.Change.Comment], x => x.Change);
            Table("income_status_periods.csv", ["id", "incomeId", "reason", "startDate", "endDate", "reducedMonthlyAmount", "comment"],
                export.Incomes.SelectMany(i => i.StatusPeriods.Select(p => (Parent: i.Id, Period: p))),
                x => [x.Period.Id, x.Parent, x.Period.Reason, x.Period.StartDate, x.Period.EndDate, x.Period.ReducedMonthlyAmount, x.Period.Comment], x => x.Period);

            Table("outgoings.csv",
                ["id", "name", "kind", "priority", "categoryId", "personId", "firstPaymentDate", "renewalDate", "renewalEveryMonths", "autoRenews", "alertPeriod",
                 "recipient", "paymentMethod", "paidFrom", "referenceLabel", "referenceValue", "website", "contactPhone", "notes"],
                export.Outgoings,
                o => [o.Id, o.Name, o.Kind, o.Priority, o.CategoryId, o.PersonId, o.FirstPaymentDate, o.Renewal?.Date, o.Renewal?.EveryMonths, o.Renewal?.AutoRenews,
                      o.Renewal?.AlertPeriod, o.Details.Recipient, o.Details.PaymentMethod, o.Details.PaidFrom, o.Details.ReferenceLabel, o.Details.ReferenceValue,
                      o.Details.Website, o.Details.ContactPhone, o.Details.Notes],
                o => o);
            Table("outgoing_amount_changes.csv", ["id", "outgoingId", "effectiveFrom", "amount", "every", "unit", "comment"],
                export.Outgoings.SelectMany(o => o.AmountChanges.Select(c => (Parent: o.Id, Change: c))),
                x => [x.Change.Id, x.Parent, x.Change.EffectiveFrom, x.Change.Amount, x.Change.Every, x.Change.Unit, x.Change.Comment], x => x.Change);
            Table("outgoing_status_periods.csv", ["id", "outgoingId", "reason", "startDate", "resumesOn", "comment"],
                export.Outgoings.SelectMany(o => o.StatusPeriods.Select(p => (Parent: o.Id, Period: p))),
                x => [x.Period.Id, x.Parent, x.Period.Reason, x.Period.StartDate, x.Period.ResumesOn, x.Period.Comment], x => x.Period);

            if (export.Options.IncludeHistory)
            {
                Table("corrections.csv", ["parentType", "parentId", "entryType", "entryId", "action", "before", "after", "reason"],
                    export.Incomes.SelectMany(i => i.Corrections.Select(c => (Type: "Income", Parent: i.Id, Correction: c)))
                        .Concat(export.Outgoings.SelectMany(o => o.Corrections.Select(c => (Type: "Outgoing", Parent: o.Id, Correction: c)))),
                    x => [x.Type, x.Parent, x.Correction.EntryType, x.Correction.EntryId, x.Correction.Action, x.Correction.Before, x.Correction.After, x.Correction.Reason],
                    x => x.Correction);
            }

            var readme = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
            using var writer = new StreamWriter(readme.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.Write(Readme(export));
        }

        return zip.ToArray();
    }

    /// <summary>
    /// One value as a CSV cell: ISO dates and times, plain invariant numbers, and text quoted when needed. Text that
    /// Excel would run as a formula (starting =, +, -, @, a tab or a carriage return) gets a leading apostrophe.
    /// </summary>
    public static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset time => time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            decimal amount => amount.ToString("0.00##", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }

    private static void Add(ZipArchive archive, string name, string[] header, IEnumerable<object?[]> rows)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.NewLine = "\r\n";
        writer.WriteLine(string.Join(',', header));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', row.Select(Cell)));
        }
    }

    private static string Readme(HouseholdExport export) => $"""
        Home Budget export for {export.Household.Name}
        Exported {export.ExportedAt:yyyy-MM-dd HH:mm zzz} by Home Budget {export.AppVersion}. Format version {export.SchemaVersion}.
        {(export.Options.IncludeHistory ? "Includes the full history: ended items, past changes and corrections." : "Includes current and future items only.")}
        {(export.Options.IncludeAudit ? "Includes who recorded or last changed each row, and when (recordedBy, recordedAt, changedBy, changedAt)." : string.Empty)}

        These are the household's raw data tables, for opening in Excel. Nothing is calculated: monthly amounts and
        budgets are worked out in the app. To restore or move your data, use the JSON download; these files can't be imported.

        The tables are joined by their id columns (e.g. incomeId in income_amount_changes.csv is an id in incomes.csv).
        Dates are yyyy-MM-dd. Amounts are plain numbers in the household's currency ({export.Household.CurrencyCode}).
        Text that starts with = + - or @ has an apostrophe added so Excel doesn't treat it as a formula.

        household.csv                 The household: name, currency, when it was exported and the format version.
        members.csv                   Who can sign in, and their role. For information only.
        people.csv                    The people whose income and outgoings are tracked; linkedMemberEmail is their login.
        categories.csv                Outgoing categories, including hidden ones.
        incomes.csv                   Income sources, each for a person (personId).
        income_amount_changes.csv     Each income amount from a date: amount received every N weeks, months or years.
        income_status_periods.csv     Leave, pauses and endings for incomes.
        outgoings.csv                 Bills, savings and allowances, with their details and renewal.
        outgoing_amount_changes.csv   Each outgoing amount from a date: amount paid every N weeks, months or years.
        outgoing_status_periods.csv   Pauses (until resumesOn) and endings for outgoings.
        {(export.Options.IncludeHistory ? "corrections.csv               Corrections and deletions of amounts and periods, with what they were and why." : string.Empty)}
        """;
}
