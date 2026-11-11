using Budget.Shared.Outgoings;

namespace Budget.Shared.Income;

/// <summary>
/// Validation for income requests, shared by the forms (so problems show as people type) and the
/// API (the real check). Each method returns error messages keyed by property name.
/// </summary>
public static class IncomeValidator
{
    public const int NameMaxLength = 100;
    public const int CommentMaxLength = 200;
    public const decimal MaxAmount = 10_000_000m;
    public const string DefaultStartingComment = "Starting amount";

    /// <summary>Shown under the amount: income is always what actually arrives.</summary>
    public const string AmountHelp = "What actually reaches your account, after tax, National Insurance, pension and other deductions.";

    public static IReadOnlyDictionary<string, string[]> Validate(CreateIncomeStreamRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Errors();
        Name(errors, nameof(request.Name), request.Name);
        Person(errors, nameof(request.PersonId), request.PersonId);
        Amount(errors, nameof(request.Amount), request.Amount, required: true);
        Schedule(errors, nameof(request.Every), request.Every, request.Unit);
        Date(errors, nameof(request.StartsOn), request.StartsOn, "Choose the date of the first payment.");
        PaidInto(errors, nameof(request.PaidInto), request.PaidInto);
        if (request.Comment?.Trim().Length > CommentMaxLength)
        {
            errors.Add(nameof(request.Comment), $"The comment can be at most {CommentMaxLength} characters.");
        }

        return errors.ToDictionary();
    }

    public static IReadOnlyDictionary<string, string[]> Validate(UpdateIncomeStreamRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Errors();
        Name(errors, nameof(request.Name), request.Name);
        Person(errors, nameof(request.PersonId), request.PersonId);
        PaidInto(errors, nameof(request.PaidInto), request.PaidInto);
        return errors.ToDictionary();
    }

    /// <summary>An account name, or "Account › Pot" (BUD-90).</summary>
    private static void PaidInto(Errors errors, string key, string? paidInto)
    {
        if (paidInto?.Trim().Length > Outgoings.OutgoingValidator.PaidFromMaxLength)
        {
            errors.Add(key, $"The account can be at most {Outgoings.OutgoingValidator.PaidFromMaxLength} characters.");
        }
    }

    public static IReadOnlyDictionary<string, string[]> Validate(SaveAmountChangeRequest request, bool isCorrection)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Errors();
        Amount(errors, nameof(request.Amount), request.Amount, required: true);
        Schedule(errors, nameof(request.Every), request.Every, request.Unit);
        Date(errors, nameof(request.EffectiveFrom), request.EffectiveFrom, "Choose the date the new amount applies from.");
        Text(errors, nameof(request.Comment), request.Comment, "Say why the amount is changing, e.g. \"3% pay rise\".");
        if (isCorrection)
        {
            Text(errors, nameof(request.CorrectionReason), request.CorrectionReason, "Say why you're correcting this.");
        }

        return errors.ToDictionary();
    }

    public static IReadOnlyDictionary<string, string[]> Validate(SaveStatusPeriodRequest request, bool isCorrection)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Errors();
        if (!Enum.IsDefined(request.Reason))
        {
            errors.Add(nameof(request.Reason), "Choose a reason.");
        }

        Date(errors, nameof(request.StartDate), request.StartDate, "Choose when it starts.");
        if (request.EndDate < request.StartDate)
        {
            errors.Add(nameof(request.EndDate), "The end date can't be before the start date.");
        }

        if (request.Reason == IncomeStatusReasonDto.Ended)
        {
            if (request.EndDate is not null)
            {
                errors.Add(nameof(request.EndDate), "An ended income has no end date.");
            }

            if (request.ReducedMonthlyAmount is not null)
            {
                errors.Add(nameof(request.ReducedMonthlyAmount), "An ended income has no reduced amount.");
            }
        }
        else
        {
            Amount(errors, nameof(request.ReducedMonthlyAmount), request.ReducedMonthlyAmount, required: false);
        }

        Text(errors, nameof(request.Comment), request.Comment, "Add a comment, e.g. \"Statutory sick pay\".");
        if (isCorrection)
        {
            Text(errors, nameof(request.CorrectionReason), request.CorrectionReason, "Say why you're correcting this.");
        }

        return errors.ToDictionary();
    }

    public static IReadOnlyDictionary<string, string[]> Validate(DeleteEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Errors();
        Text(errors, nameof(request.Reason), request.Reason, "Say why you're deleting this.");
        return errors.ToDictionary();
    }

    private static void Name(Errors errors, string property, string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add(property, "Enter a name for this income, e.g. \"Chris – salary\".");
        }
        else if (trimmed.Length > NameMaxLength)
        {
            errors.Add(property, $"The name can be at most {NameMaxLength} characters.");
        }
    }

    private static void Person(Errors errors, string property, Guid personId)
    {
        if (personId == Guid.Empty)
        {
            errors.Add(property, "Choose whose income this is.");
        }
    }

    private static void Amount(Errors errors, string property, decimal? amount, bool required)
    {
        if (amount is not { } value)
        {
            if (required)
            {
                errors.Add(property, "Enter the amount received.");
            }

            return;
        }

        if (value <= 0)
        {
            errors.Add(property, "The amount must be more than zero.");
        }
        else if (value > MaxAmount)
        {
            errors.Add(property, "That amount looks too large. Check for extra zeros.");
        }
        else if (decimal.Round(value, 2) != value)
        {
            errors.Add(property, "Use pounds and pence, with at most 2 decimal places.");
        }
    }

    private static void Schedule(Errors errors, string property, int every, PaymentUnitDto unit)
    {
        if (every is < 1 or > OutgoingValidator.MaxEvery)
        {
            errors.Add(property, $"Enter a number from 1 to {OutgoingValidator.MaxEvery}.");
        }

        if (!Enum.IsDefined(unit))
        {
            errors.Add(nameof(SaveAmountChangeRequest.Unit), "Choose weeks, months or years.");
        }
    }

    private static void Date(Errors errors, string property, DateOnly? date, string message)
    {
        if (date is null)
        {
            errors.Add(property, message);
        }
    }

    private static void Text(Errors errors, string property, string? text, string requiredMessage)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add(property, requiredMessage);
        }
        else if (trimmed.Length > CommentMaxLength)
        {
            errors.Add(property, $"This can be at most {CommentMaxLength} characters.");
        }
    }

    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = [];

        public void Add(string property, string message)
        {
            if (!_errors.TryGetValue(property, out var list))
            {
                _errors[property] = list = [];
            }

            list.Add(message);
        }

        public IReadOnlyDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}

/// <summary>Words for income reasons and money, used by the client.</summary>
public static class IncomeLabels
{
    public static string Reason(IncomeStatusReasonDto reason) => reason switch
    {
        IncomeStatusReasonDto.Holiday => "Holiday",
        IncomeStatusReasonDto.SickLeave => "Sick leave",
        IncomeStatusReasonDto.UnpaidLeave => "Unpaid leave",
        IncomeStatusReasonDto.ParentalLeave => "Parental leave",
        IncomeStatusReasonDto.Ended => "Ended",
        IncomeStatusReasonDto.Other => "Other",
        _ => reason.ToString(),
    };
}

/// <summary>Formats money the same way everywhere, e.g. "£2,307.69" or "−£11.45".</summary>
public static class Money
{
    /// <summary>A minus sign joined to the amount, so a line can never break between them (BUD-128).</summary>
    public const string Minus = "\u2212\u2060";

    /// <summary>A plus sign joined to the amount, so a line can never break between them (BUD-128).</summary>
    public const string Plus = "+\u2060";

    public static string Format(decimal amount, string currencyCode = "GBP") =>
        (amount < 0 ? Minus : string.Empty) + Symbol(currencyCode) + Math.Abs(amount).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An amount coming in or going out, e.g. "+£2,307.69" or "−£11.45".</summary>
    public static string Signed(decimal amount, bool coming, string currencyCode = "GBP") =>
        (coming ? Plus : Minus) + Format(Math.Abs(amount), currencyCode);

    public static string Symbol(string currencyCode) => currencyCode switch
    {
        "GBP" => "£",
        "EUR" => "€",
        "USD" => "$",
        _ => currencyCode + " ",
    };
}
