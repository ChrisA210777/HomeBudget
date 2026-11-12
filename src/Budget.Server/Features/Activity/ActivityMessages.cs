using System.Globalization;
using Budget.Domain.Activity;
using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;

namespace Budget.Server.Features.Activity;

/// <summary>
/// The wording of recent activity (BUD-139), kept together so it reads the same everywhere. Each sentence follows the
/// person's name, e.g. "Chris" + "paused Netflix from 1 Dec 2026". Amounts are written as <c>{{480.00}}</c> and shown in
/// the household's currency. Each change's handler records one of these before it saves.
/// </summary>
public static class ActivityMessages
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ---- Income ----

    public static ActivityEntry IncomeAdded(Guid householdId, IncomeStream income, decimal amount, PaymentSchedule schedule) =>
        Entry(householdId, ActivityArea.Income, income.Id, $"added {Adjective(schedule)} income: {income.Name}, {Money(amount)}");

    public static ActivityEntry IncomeUpdated(Guid householdId, IncomeStream income) =>
        Entry(householdId, ActivityArea.Income, income.Id, $"updated income: {income.Name}", merge: true);

    public static ActivityEntry IncomeDeleted(Guid householdId, IncomeStream income) =>
        Entry(householdId, ActivityArea.Income, income.Id, $"deleted income: {income.Name}");

    public static ActivityEntry IncomeAmountChanged(Guid householdId, IncomeStream income, decimal amount, PaymentSchedule schedule, DateOnly from) =>
        Entry(householdId, ActivityArea.Income, income.Id, $"changed {income.Name} to {Money(amount)} {Every(schedule)} from {Date(from)}");

    public static ActivityEntry IncomeLeaveAdded(Guid householdId, IncomeStream income, IncomeStatusReason reason, DateOnly start, DateOnly? end) =>
        Entry(householdId, ActivityArea.Income, income.Id, reason == IncomeStatusReason.Ended
            ? $"ended {income.Name} from {Date(start)}"
            : $"added {Leave(reason)} to {income.Name} from {Date(start)}{Until(end)}");

    public static ActivityEntry IncomeCorrected(Guid householdId, IncomeStream income, string what, string reason) =>
        Entry(householdId, ActivityArea.Income, income.Id, $"corrected {what} for {income.Name}: {reason}");

    public static ActivityEntry IncomeEntryDeleted(Guid householdId, IncomeStream income, string what, string reason) =>
        Entry(householdId, ActivityArea.Income, income.Id, $"removed {what} from {income.Name}: {reason}");

    // ---- Outgoings ----

    public static ActivityEntry OutgoingAdded(Guid householdId, Outgoing outgoing, decimal amount, PaymentSchedule schedule) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"added a new {Adjective(schedule)} {Kind(outgoing.Kind)}: {outgoing.Name}, {Money(amount)}");

    public static ActivityEntry OutgoingUpdated(Guid householdId, Outgoing outgoing) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"updated {outgoing.Name}", merge: true);

    public static ActivityEntry OutgoingRenewed(Guid householdId, Outgoing outgoing, DateOnly nextRenewal) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"renewed {outgoing.Name}, next due for renewal on {Date(nextRenewal)}");

    public static ActivityEntry OutgoingDeleted(Guid householdId, Outgoing outgoing) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"deleted {outgoing.Name}");

    public static ActivityEntry OutgoingAmountChanged(Guid householdId, Outgoing outgoing, decimal amount, PaymentSchedule schedule, DateOnly from) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"changed {outgoing.Name} to {Money(amount)} {Every(schedule)} from {Date(from)}");

    public static ActivityEntry OutgoingPaused(Guid householdId, Outgoing outgoing, DateOnly from, DateOnly? resumes) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"paused {outgoing.Name} from {Date(from)}{Resuming(resumes)}");

    public static ActivityEntry OutgoingEnded(Guid householdId, Outgoing outgoing, DateOnly from) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"ended {outgoing.Name} from {Date(from)}");

    public static ActivityEntry OutgoingCorrected(Guid householdId, Outgoing outgoing, string what, string reason) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"corrected {what} for {outgoing.Name}: {reason}");

    public static ActivityEntry OutgoingEntryDeleted(Guid householdId, Outgoing outgoing, string what, string reason) =>
        Entry(householdId, ActivityArea.Outgoings, outgoing.Id, $"removed {what} from {outgoing.Name}: {reason}");

    // ---- Categories ----

    public static ActivityEntry CategoryAdded(Guid householdId, Guid categoryId, string name) =>
        Entry(householdId, ActivityArea.Categories, categoryId, $"added the category {name}");

    public static ActivityEntry CategoryUpdated(Guid householdId, Guid categoryId, string name, bool? hidden) =>
        Entry(householdId, ActivityArea.Categories, categoryId, hidden switch
        {
            true => $"hid the category {name}",
            false => $"showed the category {name} again",
            null => $"updated the category {name}",
        }, merge: hidden is null);

    public static ActivityEntry CategoryDeleted(Guid householdId, Guid categoryId, string name) =>
        Entry(householdId, ActivityArea.Categories, categoryId, $"deleted the category {name}");

    // ---- Transfers ----

    /// <param name="label">From <see cref="TransferLabel"/>: "Bills top-up", or "from Chris current to Joint".</param>
    public static ActivityEntry TransferAdded(Guid householdId, Guid transferId, string label, decimal amount, PaymentSchedule schedule) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"added a {Adjective(schedule)} transfer: {label}, {Money(amount)}");

    public static ActivityEntry TransferUpdated(Guid householdId, Guid transferId, string label) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"updated the transfer {label}", merge: true);

    public static ActivityEntry TransferDeleted(Guid householdId, Guid transferId, string label) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"deleted the transfer {label}");

    public static ActivityEntry TransferAmountChanged(Guid householdId, Guid transferId, string label, decimal amount, PaymentSchedule schedule, DateOnly from) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"changed the transfer {label} to {Money(amount)} {Every(schedule)} from {Date(from)}");

    public static ActivityEntry TransferPaused(Guid householdId, Guid transferId, string label, DateOnly from, DateOnly? resumes) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"paused the transfer {label} from {Date(from)}{Resuming(resumes)}");

    public static ActivityEntry TransferEnded(Guid householdId, Guid transferId, string label, DateOnly from) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"ended the transfer {label} from {Date(from)}");

    public static ActivityEntry TransferCorrected(Guid householdId, Guid transferId, string label, string what, string reason) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"corrected {what} for the transfer {label}: {reason}");

    public static ActivityEntry TransferEntryDeleted(Guid householdId, Guid transferId, string label, string what, string reason) =>
        Entry(householdId, ActivityArea.Transfers, transferId, $"removed {what} from the transfer {label}: {reason}");

    /// <summary>"Bills top-up", or "from Chris current to Joint" for a transfer with no name.</summary>
    public static string TransferLabel(string? name, string? from, string? to) =>
        string.IsNullOrWhiteSpace(name) ? $"from {from} to {to}" : name;

    // ---- Accounts ----

    public static ActivityEntry AccountAdded(Guid householdId, Guid accountId, string displayName, bool isPot) =>
        Entry(householdId, ActivityArea.Accounts, accountId, $"added the {(isPot ? "pot" : "account")} {displayName}");

    public static ActivityEntry AccountUpdated(Guid householdId, Guid accountId, string displayName) =>
        Entry(householdId, ActivityArea.Accounts, accountId, $"updated {displayName}", merge: true);

    public static ActivityEntry AccountArchived(Guid householdId, Guid accountId, string displayName, bool archived) =>
        Entry(householdId, ActivityArea.Accounts, accountId, archived ? $"archived {displayName}" : $"restored {displayName}");

    public static ActivityEntry AccountMerged(Guid householdId, Guid intoId, string from, string into) =>
        Entry(householdId, ActivityArea.Accounts, intoId, $"merged {from} into {into}");

    public static ActivityEntry AccountMonthStart(Guid householdId, Guid accountId, string displayName, string? payment) =>
        Entry(householdId, ActivityArea.Accounts, accountId, payment is null
            ? $"set {displayName}'s month to start on the 1st"
            : $"set {displayName}'s month to start when {payment} arrives");

    // ---- People ----

    public static ActivityEntry PersonAdded(Guid householdId, Guid personId, string name) =>
        Entry(householdId, ActivityArea.People, personId, $"added {name} to the household's people");

    public static ActivityEntry PersonRenamed(Guid householdId, Guid personId, string from, string to) =>
        Entry(householdId, ActivityArea.People, personId, $"renamed {from} to {to}");

    // ---- The household and its members ----

    public static ActivityEntry HouseholdCreated(Guid householdId, string name) =>
        Entry(householdId, ActivityArea.Household, null, $"created the household {name}");

    public static ActivityEntry HouseholdRenamed(Guid householdId, string from, string to) =>
        Entry(householdId, ActivityArea.Household, null, $"renamed the household from {from} to {to}");

    public static ActivityEntry TransfersTurned(Guid householdId, bool on) =>
        Entry(householdId, ActivityArea.Household, null, on ? "turned on accounts, transfers and Cash flow" : "turned off transfers and Cash flow");

    public static ActivityEntry Imported(Guid householdId, bool replaced) =>
        Entry(householdId, ActivityArea.Household, null, replaced
            ? "replaced the household's data from an export file"
            : "created this household from an export file");

    public static ActivityEntry MemberInvited(Guid householdId, string who, HouseholdRole role) =>
        Entry(householdId, ActivityArea.Members, null, $"invited {who} as {Role(role)}");

    public static ActivityEntry InvitationCancelled(Guid householdId, string who) =>
        Entry(householdId, ActivityArea.Members, null, $"cancelled the invitation for {who}");

    public static ActivityEntry MemberJoined(Guid householdId, HouseholdRole role) =>
        Entry(householdId, ActivityArea.Members, null, $"joined the household as {Role(role)}");

    public static ActivityEntry MemberRoleChanged(Guid householdId, string who, HouseholdRole role) =>
        Entry(householdId, ActivityArea.Members, null, $"made {who} {Role(role)}");

    public static ActivityEntry MemberRemoved(Guid householdId, string who) =>
        Entry(householdId, ActivityArea.Members, null, $"removed {who} from the household");

    public static ActivityEntry MemberLeft(Guid householdId) =>
        Entry(householdId, ActivityArea.Members, null, "left the household");

    // ---- Wording helpers ----

    /// <summary>An amount, shown in the household's currency: <c>{{480.00}}</c>.</summary>
    public static string Money(decimal amount) => "{{" + amount.ToString("0.00", Invariant) + "}}";

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", Invariant);

    /// <summary>"a month", "a week", "every 4 weeks".</summary>
    public static string Every(PaymentSchedule schedule) => schedule.Every == 1
        ? $"a {Unit(schedule.Unit)}"
        : $"every {schedule.Every} {Unit(schedule.Unit)}s";

    /// <summary>"monthly", "yearly", "4-weekly", "quarterly".</summary>
    public static string Adjective(PaymentSchedule schedule) => (schedule.Every, schedule.Unit) switch
    {
        (1, PaymentUnit.Week) => "weekly",
        (1, PaymentUnit.Month) => "monthly",
        (1, PaymentUnit.Year) => "yearly",
        (3, PaymentUnit.Month) => "quarterly",
        (var every, var unit) => $"{every}-{Unit(unit)}ly",
    };

    private static string Unit(PaymentUnit unit) => unit switch
    {
        PaymentUnit.Week => "week",
        PaymentUnit.Month => "month",
        _ => "year",
    };

    private static string Kind(OutgoingKind kind) => kind switch
    {
        OutgoingKind.Bill => "bill",
        OutgoingKind.Savings => "saving",
        _ => "allowance",
    };

    private static string Leave(IncomeStatusReason reason) => reason switch
    {
        IncomeStatusReason.Holiday => "a holiday",
        IncomeStatusReason.SickLeave => "sick leave",
        IncomeStatusReason.UnpaidLeave => "unpaid leave",
        IncomeStatusReason.ParentalLeave => "parental leave",
        _ => "a break",
    };

    private static string Role(HouseholdRole role) => role switch
    {
        HouseholdRole.Admin => "an Admin",
        HouseholdRole.User => "a User",
        _ => "a Viewer",
    };

    private static string Until(DateOnly? end) => end is { } date ? $" to {Date(date)}" : string.Empty;

    private static string Resuming(DateOnly? resumes) => resumes is { } date ? $", resuming {Date(date)}" : string.Empty;

    private static ActivityEntry Entry(Guid householdId, ActivityArea area, Guid? subjectId, string summary, bool merge = false) =>
        ActivityEntry.Create(householdId, area, subjectId, summary, merge ? $"{area}:{subjectId}:edit" : null);
}
