using Budget.Shared.Outgoings;

namespace Budget.Shared.Transfers;

/// <summary>Transfers between accounts (BUD-91, ADR-014). Only when the household has "Accounts and transfers" on.</summary>
public static class TransferRoutes
{
    /// <summary>GET (Viewers): every transfer. POST (Users): add one.</summary>
    public static string Transfers(Guid householdId) => $"/api/households/{householdId}/transfers";

    /// <summary>GET: one transfer with its history. PUT: change its details. DELETE: remove it.</summary>
    public static string Transfer(Guid householdId, Guid transferId) => $"{Transfers(householdId)}/{transferId}";

    /// <summary>POST: a new amount from a date.</summary>
    public static string AmountChanges(Guid householdId, Guid transferId) => $"{Transfer(householdId, transferId)}/amount-changes";

    /// <summary>PUT: correct an amount change. DELETE (with a reason): remove one recorded by mistake.</summary>
    public static string AmountChange(Guid householdId, Guid transferId, Guid changeId) => $"{AmountChanges(householdId, transferId)}/{changeId}";

    /// <summary>POST: pause or end it.</summary>
    public static string StatusPeriods(Guid householdId, Guid transferId) => $"{Transfer(householdId, transferId)}/status-periods";

    /// <summary>PUT: correct a pause or ending. DELETE (with a reason): remove one recorded by mistake.</summary>
    public static string StatusPeriod(Guid householdId, Guid transferId, Guid periodId) => $"{StatusPeriods(householdId, transferId)}/{periodId}";
}

/// <summary>Add a transfer.</summary>
public sealed record CreateTransferRequest
{
    public string? Name { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public decimal? Amount { get; init; }

    public int Every { get; init; } = 1;

    public PaymentUnitDto Unit { get; init; } = PaymentUnitDto.Month;

    public DateOnly? FirstPaymentDate { get; init; }

    public string? Notes { get; init; }
}

/// <summary>Change a transfer's details. Amounts, pauses and its end have their own history.</summary>
public sealed record UpdateTransferRequest
{
    public string? Name { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public string? Notes { get; init; }

    public string? Version { get; init; }
}

/// <summary>A transfer in the list.</summary>
/// <param name="FromName">The account it goes from, e.g. "Chris current" or "Joint Monzo › Holiday".</param>
/// <param name="ToAccountId">The account (not pot) it goes into, for grouping.</param>
/// <param name="Amount">What's moved today (or the first amount, if it hasn't started).</param>
/// <param name="Status">e.g. "Paused until 1 Mar 2027" or "Ended from 1 Jun 2026"; null when active.</param>
public sealed record TransferSummary(
    Guid Id,
    string? Name,
    Guid FromAccountId,
    string FromName,
    Guid ToAccountId,
    string ToName,
    Guid ToTopAccountId,
    string ToTopAccountName,
    decimal Amount,
    int Every,
    PaymentUnitDto Unit,
    decimal MonthlyAmount,
    DateOnly? NextPayment,
    string? Status,
    bool IsEnded);

public sealed record TransfersResponse(IReadOnlyList<TransferSummary> Transfers, string CurrencyCode);

/// <summary>A transfer with its full history, for its own page.</summary>
public sealed record TransferDetailsResponse(
    Guid Id,
    string? Name,
    Guid FromAccountId,
    string FromName,
    Guid ToAccountId,
    string ToName,
    decimal Amount,
    int Every,
    PaymentUnitDto Unit,
    decimal MonthlyAmount,
    DateOnly FirstPaymentDate,
    DateOnly? EndsOn,
    DateOnly? NextPayment,
    string? Status,
    string? Notes,
    IReadOnlyList<DateOnly> UpcomingPayments,
    IReadOnlyList<OutgoingAmountEntry> AmountChanges,
    IReadOnlyList<OutgoingStatusEntry> StatusPeriods,
    IReadOnlyList<OutgoingCorrectionEntry> Corrections,
    string CurrencyCode,
    string Version);

/// <summary>Shared by the form and the API.</summary>
public static class TransferValidator
{
    public const int NameMaxLength = 100;
    public const int NotesMaxLength = 2000;

    public static IReadOnlyDictionary<string, string[]> Validate(CreateTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = Details(request.Name, request.FromAccountId, request.ToAccountId, request.Notes);
        var amount = OutgoingValidator.Validate(
            new SaveOutgoingAmountRequest { Amount = request.Amount, Every = request.Every, Unit = request.Unit, EffectiveFrom = request.FirstPaymentDate, Comment = "First amount" },
            isCorrection: false);
        foreach (var (key, messages) in amount)
        {
            if (key == nameof(SaveOutgoingAmountRequest.EffectiveFrom))
            {
                errors[nameof(request.FirstPaymentDate)] = ["Choose the date of the first transfer."];
            }
            else
            {
                errors[key] = messages;
            }
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(UpdateTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Details(request.Name, request.FromAccountId, request.ToAccountId, request.Notes);
    }

    private static Dictionary<string, string[]> Details(string? name, Guid? from, Guid? to, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        if (name?.Trim().Length > NameMaxLength)
        {
            errors[nameof(CreateTransferRequest.Name)] = [$"Use {NameMaxLength} characters or fewer."];
        }

        if (from is null || from == Guid.Empty)
        {
            errors[nameof(CreateTransferRequest.FromAccountId)] = ["Choose the account it comes from."];
        }

        if (to is null || to == Guid.Empty)
        {
            errors[nameof(CreateTransferRequest.ToAccountId)] = ["Choose the account it goes into."];
        }
        else if (to == from)
        {
            errors[nameof(CreateTransferRequest.ToAccountId)] = ["Choose a different account from the one it comes from."];
        }

        if (notes?.Trim().Length > NotesMaxLength)
        {
            errors[nameof(CreateTransferRequest.Notes)] = [$"Use {NotesMaxLength} characters or fewer."];
        }

        return errors;
    }
}
