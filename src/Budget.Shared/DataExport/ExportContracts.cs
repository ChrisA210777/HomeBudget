using System.Text.Json.Serialization;

namespace Budget.Shared.DataExport;

// The household export file, schema version 1 (BUD-64, ADR-012). See "Data Export & Import Format" in Confluence.
//
// DON'T change these types without bumping ExportFormat.CurrentSchemaVersion, adding an upgrader from the previous
// version and a golden file for the new one. A schema snapshot test fails if they change without a bump.
//
// Enum values are written as strings (the names listed in ExportValues), not as this app's enum types, so renaming
// an enum in the app can't silently change the file format.

/// <summary>The whole export file.</summary>
public sealed record HouseholdExport
{
    /// <summary>Always <see cref="ExportFormat.FormatName"/>.</summary>
    public string Format { get; init; } = ExportFormat.FormatName;

    public int SchemaVersion { get; init; } = ExportFormat.CurrentSchemaVersion;

    public DateTimeOffset ExportedAt { get; init; }

    /// <summary>The version of Home Budget that wrote the file, for information.</summary>
    public string AppVersion { get; init; } = string.Empty;

    public ExportOptions Options { get; init; } = new();

    public ExportedHousehold Household { get; init; } = new();

    /// <summary>For information only: logins can't come from a file, so members are invited again after an import.</summary>
    public IReadOnlyList<ExportedMember> Members { get; init; } = [];

    public IReadOnlyList<ExportedPerson> People { get; init; } = [];

    public IReadOnlyList<ExportedCategory> Categories { get; init; } = [];

    /// <summary>Accounts and their pots (version 2, BUD-89). Outgoings name theirs in <c>details.paidFrom</c>.</summary>
    public IReadOnlyList<ExportedAccount> Accounts { get; init; } = [];

    public IReadOnlyList<ExportedIncome> Incomes { get; init; } = [];

    public IReadOnlyList<ExportedOutgoing> Outgoings { get; init; } = [];
}

public sealed record ExportOptions
{
    /// <summary>Everything, including ended items, past changes and corrections. Otherwise only what's current or future.</summary>
    public bool IncludeHistory { get; init; }

    /// <summary>Who recorded or changed each item, by display name, and when.</summary>
    public bool IncludeAudit { get; init; }
}

public sealed record ExportedHousehold
{
    public string Name { get; init; } = string.Empty;

    /// <summary>ISO 4217, e.g. "GBP".</summary>
    public string CurrencyCode { get; init; } = string.Empty;
}

public sealed record ExportedMember
{
    public string DisplayName { get; init; } = string.Empty;

    public string? Email { get; init; }

    /// <summary>Viewer, User or Admin.</summary>
    public string Role { get; init; } = string.Empty;

    public bool IsOwner { get; init; }
}

/// <summary>Who recorded or last changed an item, and when. Only filled in when the export includes audit details.</summary>
public abstract record ExportedAudited
{
    [JsonPropertyOrder(100)]
    public string? RecordedBy { get; init; }

    [JsonPropertyOrder(101)]
    public DateTimeOffset? RecordedAt { get; init; }

    [JsonPropertyOrder(102)]
    public string? ChangedBy { get; init; }

    [JsonPropertyOrder(103)]
    public DateTimeOffset? ChangedAt { get; init; }
}

public sealed record ExportedPerson : ExportedAudited
{
    /// <summary>Links items within the file only; new ids are made on import.</summary>
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The email of the member whose login this person is, if any.</summary>
    public string? LinkedMemberEmail { get; init; }
}

public sealed record ExportedCategory : ExportedAudited
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public bool IsHidden { get; init; }

    /// <summary>A key from the app's category icons, e.g. "home".</summary>
    public string Icon { get; init; } = string.Empty;
}

/// <summary>An amount from a date, paid or received every <see cref="Every"/> <see cref="Unit"/>s.</summary>
public sealed record ExportedAmountChange : ExportedAudited
{
    public Guid Id { get; init; }

    public DateOnly EffectiveFrom { get; init; }

    public decimal Amount { get; init; }

    public int Every { get; init; } = 1;

    /// <summary>Week, Month or Year.</summary>
    public string Unit { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;
}

/// <summary>A record that an entry was corrected or deleted, with what it was and why.</summary>
public sealed record ExportedCorrection : ExportedAudited
{
    /// <summary>AmountChange or StatusPeriod.</summary>
    public string EntryType { get; init; } = string.Empty;

    /// <summary>The <c>id</c> of the amount change or status period in this file (or of one that was deleted).</summary>
    public Guid EntryId { get; init; }

    /// <summary>Edited or Deleted.</summary>
    public string Action { get; init; } = string.Empty;

    public string Before { get; init; } = string.Empty;

    public string? After { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed record ExportedIncome : ExportedAudited
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid PersonId { get; init; }

    /// <summary>The account, or "Account › Pot", it's paid into (version 3, BUD-90).</summary>
    public string? PaidInto { get; init; }

    public IReadOnlyList<ExportedAmountChange> AmountChanges { get; init; } = [];

    public IReadOnlyList<ExportedIncomeStatusPeriod> StatusPeriods { get; init; } = [];

    public IReadOnlyList<ExportedCorrection> Corrections { get; init; } = [];
}

public sealed record ExportedIncomeStatusPeriod : ExportedAudited
{
    public Guid Id { get; init; }

    /// <summary>Holiday, SickLeave, UnpaidLeave, ParentalLeave, Ended or Other.</summary>
    public string Reason { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }

    /// <summary>The last day; <c>null</c> for open-ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>The monthly pay during the period, if it's reduced rather than nothing.</summary>
    public decimal? ReducedMonthlyAmount { get; init; }

    public string Comment { get; init; } = string.Empty;
}

public sealed record ExportedOutgoing : ExportedAudited
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Bill, Savings or Allowance.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Essential, Preferred or Optional.</summary>
    public string Priority { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    /// <summary>Who it's for; <c>null</c> for the whole household.</summary>
    public Guid? PersonId { get; init; }

    public DateOnly FirstPaymentDate { get; init; }

    /// <summary><c>null</c> if it doesn't renew.</summary>
    public ExportedRenewal? Renewal { get; init; }

    public ExportedOutgoingDetails Details { get; init; } = new();

    public IReadOnlyList<ExportedAmountChange> AmountChanges { get; init; } = [];

    public IReadOnlyList<ExportedOutgoingStatusPeriod> StatusPeriods { get; init; } = [];

    public IReadOnlyList<ExportedCorrection> Corrections { get; init; } = [];
}

public sealed record ExportedRenewal
{
    public DateOnly Date { get; init; }

    public int EveryMonths { get; init; } = 12;

    public bool AutoRenews { get; init; }

    /// <summary>None, OneDay, TwoDays, ThreeDays, OneWeek, TwoWeeks, OneMonth, TwoMonths, ThreeMonths or SixMonths.</summary>
    public string AlertPeriod { get; init; } = string.Empty;
}

/// <summary>An account and its pots (BUD-89). Names are unique: accounts in the file, and pots in their account.</summary>
public sealed record ExportedAccount
{
    public string Name { get; init; } = string.Empty;

    /// <summary>The bank, e.g. "Monzo".</summary>
    public string? Provider { get; init; }

    /// <summary>The person (an id in <see cref="HouseholdExport.People"/>) whose account it is, or <c>null</c> for the household's.</summary>
    public Guid? OwnerPersonId { get; init; }

    public bool Archived { get; init; }

    public IReadOnlyList<ExportedPot> Pots { get; init; } = [];
}

public sealed record ExportedPot
{
    public string Name { get; init; } = string.Empty;

    public bool Archived { get; init; }
}

public sealed record ExportedOutgoingDetails
{
    public string? Recipient { get; init; }

    /// <summary>DirectDebit, StandingOrder, Cash, CreditCard, DebitCard, Cheque, BankTransfer or Other.</summary>
    public string? PaymentMethod { get; init; }

    /// <summary>The account, or "Account › Pot", it's paid from (an account in <see cref="HouseholdExport.Accounts"/> from version 2).</summary>
    public string? PaidFrom { get; init; }

    public string? ReferenceLabel { get; init; }

    public string? ReferenceValue { get; init; }

    public string? Website { get; init; }

    public string? ContactPhone { get; init; }

    public string? Notes { get; init; }
}

public sealed record ExportedOutgoingStatusPeriod : ExportedAudited
{
    public Guid Id { get; init; }

    /// <summary>Paused or Ended.</summary>
    public string Reason { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }

    /// <summary>For a pause, the first payment after it; <c>null</c> until further notice. Always <c>null</c> for Ended.</summary>
    public DateOnly? ResumesOn { get; init; }

    public string Comment { get; init; } = string.Empty;
}
