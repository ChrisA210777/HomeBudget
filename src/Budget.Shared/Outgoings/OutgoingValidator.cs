using System.Globalization;
using System.Text.RegularExpressions;

namespace Budget.Shared.Outgoings;

/// <summary>
/// Validation and wording for outgoings, shared by the form (so problems show as people type) and
/// the API (the real check). <see cref="Validate"/> returns error messages keyed by property name.
/// </summary>
public static partial class OutgoingValidator
{
    public const int NameMaxLength = 100;
    public const int CommentMaxLength = 200;
    public const int RecipientMaxLength = 100;
    public const int PaidFromMaxLength = 100;
    public const int ReferenceLabelMaxLength = 50;
    public const int ReferenceValueMaxLength = 100;
    public const int WebsiteMaxLength = 300;
    public const int ContactPhoneMaxLength = 30;
    public const int NotesMaxLength = 2000;
    public const int MaxEvery = 99;
    public const int MaxRenewalEveryMonths = 120;
    public const decimal MaxAmount = 10_000_000m;
    public const string DefaultStartingComment = "Starting amount";

    public const string SensitiveDataWarning =
        "That looks like a card number. Don't store card numbers, bank details or passwords here.";

    public static IReadOnlyDictionary<string, string[]> Validate(CreateOutgoingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                errors[key] = list = [];
            }

            list.Add(message);
        }

        void Max(string key, string? text, int max, string what)
        {
            if (text?.Trim().Length > max)
            {
                Add(key, $"{what} can be at most {max} characters.");
            }
        }

        if (!Enum.IsDefined(request.Kind))
        {
            Add(nameof(request.Kind), "Choose a bill, savings or an allowance.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            Add(nameof(request.Name), "Enter a name, e.g. \"Council tax\".");
        }

        Max(nameof(request.Name), request.Name, NameMaxLength, "The name");

        if (request.CategoryId == Guid.Empty)
        {
            Add(nameof(request.CategoryId), "Choose a category.");
        }

        if (request.Priority is not { } priority || !Enum.IsDefined(priority))
        {
            Add(nameof(request.Priority), "Choose how important it is.");
        }

        switch (request.Amount)
        {
            case null:
                Add(nameof(request.Amount), "Enter the amount.");
                break;
            case <= 0:
                Add(nameof(request.Amount), "The amount must be more than £0.");
                break;
            case > MaxAmount:
                Add(nameof(request.Amount), "That amount is too large.");
                break;
            case { } amount when decimal.Round(amount, 2) != amount:
                Add(nameof(request.Amount), "Use pounds and pence, e.g. 12.50.");
                break;
        }

        if (request.Every is < 1 or > MaxEvery)
        {
            Add(nameof(request.Every), $"Enter a number from 1 to {MaxEvery}.");
        }

        if (!Enum.IsDefined(request.Unit))
        {
            Add(nameof(request.Unit), "Choose weeks, months or years.");
        }

        if (request.FirstPaymentDate is null)
        {
            Add(nameof(request.FirstPaymentDate), "Choose the date of the first payment.");
        }

        Max(nameof(request.Comment), request.Comment, CommentMaxLength, "The comment");
        Max(nameof(request.Recipient), request.Recipient, RecipientMaxLength, "This");
        Max(nameof(request.PaidFrom), request.PaidFrom, PaidFromMaxLength, "The account name");
        Max(nameof(request.ReferenceLabel), request.ReferenceLabel, ReferenceLabelMaxLength, "The reference name");
        Max(nameof(request.ReferenceValue), request.ReferenceValue, ReferenceValueMaxLength, "The reference");
        Max(nameof(request.ContactPhone), request.ContactPhone, ContactPhoneMaxLength, "The phone number");
        Max(nameof(request.Notes), request.Notes, NotesMaxLength, "Notes");

        if (request.PaymentMethod is { } method && !Enum.IsDefined(method))
        {
            Add(nameof(request.PaymentMethod), "Choose a payment method.");
        }

        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            if (request.Website.Trim().Length > WebsiteMaxLength)
            {
                Add(nameof(request.Website), $"The website can be at most {WebsiteMaxLength} characters.");
            }
            else if (!IsWebsite(request.Website))
            {
                Add(nameof(request.Website), "Enter a website address, e.g. example.com.");
            }
        }

        if (request.RenewalDate is not null)
        {
            if (request.Kind == OutgoingKindDto.Allowance)
            {
                Add(nameof(request.RenewalDate), "Allowances don't renew.");
            }

            if (request.RenewalEveryMonths is < 1 or > MaxRenewalEveryMonths)
            {
                Add(nameof(request.RenewalEveryMonths), $"Enter a number of months from 1 to {MaxRenewalEveryMonths}.");
            }

            if (!Enum.IsDefined(request.AlertPeriod))
            {
                Add(nameof(request.AlertPeriod), "Choose when to be reminded.");
            }
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    /// <summary>The same check the domain makes: http(s), with a dot in the host. "https://" is optional.</summary>
    public static bool IsWebsite(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var withScheme = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        return Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Host.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>The monthly amount for the budget: weekly × 52 ÷ 12, yearly ÷ 12 (ADR-011). Not rounded.</summary>
    public static decimal MonthlyEquivalent(decimal amount, int every, PaymentUnitDto unit)
    {
        if (every < 1)
        {
            return 0m;
        }

        return unit switch
        {
            PaymentUnitDto.Week => amount * 52m / 12m / every,
            PaymentUnitDto.Month => amount / every,
            PaymentUnitDto.Year => amount / (12m * every),
            _ => 0m,
        };
    }

    /// <summary>e.g. "£480.00 a year = £40.00 a month"; <c>null</c> until the amount and frequency make sense.</summary>
    public static string? DescribeMonthlyEquivalent(decimal? amount, int every, PaymentUnitDto unit)
    {
        if (amount is not > 0 || every is < 1 or > MaxEvery)
        {
            return null;
        }

        var paid = $"{Money(amount.Value)} {DescribeSchedule(every, unit)}";
        if (every == 1 && unit == PaymentUnitDto.Month)
        {
            return paid;
        }

        var monthly = Math.Round(MonthlyEquivalent(amount.Value, every, unit), 2, MidpointRounding.AwayFromZero);
        return $"{paid} = {Money(monthly)} a month";
    }

    /// <summary>"a month", "every 2 months", "a year", "every 4 weeks".</summary>
    public static string DescribeSchedule(int every, PaymentUnitDto unit)
    {
        var word = unit.ToString().ToLowerInvariant();
        return every == 1 ? $"a {word}" : $"every {every} {word}s";
    }

    /// <summary>True when the text contains 13–19 digits in a row, allowing spaces or dashes between them.</summary>
    public static bool LooksLikeCardNumber(string? text) => text is not null && CardNumber().IsMatch(text);

    /// <summary>Wording that depends on the kind, e.g. who the money goes to.</summary>
    public static string RecipientLabel(OutgoingKindDto kind) => kind switch
    {
        OutgoingKindDto.Savings => "Savings pot or account",
        _ => "Paid to",
    };

    public static string RecipientHelp(OutgoingKindDto kind) => kind switch
    {
        OutgoingKindDto.Savings => "Where the money is put aside, e.g. \"Holiday ISA\"",
        _ => "The company or person, e.g. \"British Gas\"",
    };

    public static string NameHelp(OutgoingKindDto kind) => kind switch
    {
        OutgoingKindDto.Savings => "For example, \"Holiday savings\"",
        OutgoingKindDto.Allowance => "For example, \"Food shopping\" or \"Fuel\"",
        _ => "For example, \"Council tax\" or \"Netflix\"",
    };

    public static string KindDescription(OutgoingKindDto kind) => kind switch
    {
        OutgoingKindDto.Savings => "Money you put aside, e.g. for holidays or replacing things",
        OutgoingKindDto.Allowance => "A spending budget, e.g. food or fuel",
        _ => "Paid to a company or person, e.g. council tax or insurance",
    };

    public static string Describe(PaymentMethodDto method) => method switch
    {
        PaymentMethodDto.DirectDebit => "Direct Debit",
        PaymentMethodDto.StandingOrder => "Standing order",
        PaymentMethodDto.CreditCard => "Credit card",
        PaymentMethodDto.DebitCard => "Debit card",
        PaymentMethodDto.BankTransfer => "Bank transfer",
        _ => method.ToString(),
    };

    public static string Describe(AlertPeriodDto period) => period switch
    {
        AlertPeriodDto.None => "No reminder",
        AlertPeriodDto.OneDay => "1 day before",
        AlertPeriodDto.TwoDays => "2 days before",
        AlertPeriodDto.ThreeDays => "3 days before",
        AlertPeriodDto.OneWeek => "1 week before",
        AlertPeriodDto.TwoWeeks => "2 weeks before",
        AlertPeriodDto.OneMonth => "1 month before",
        AlertPeriodDto.TwoMonths => "2 months before",
        AlertPeriodDto.ThreeMonths => "3 months before",
        AlertPeriodDto.SixMonths => "6 months before",
        _ => period.ToString(),
    };

    public static string Describe(OutgoingPriorityDto priority) => priority switch
    {
        OutgoingPriorityDto.Essential => "Essential – has to be paid",
        OutgoingPriorityDto.Preferred => "Preferred – should be paid if possible",
        OutgoingPriorityDto.Optional => "Optional – a choice that could be cut",
        _ => priority.ToString(),
    };

    private static string Money(decimal amount) => "£" + amount.ToString("N2", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)")]
    private static partial Regex CardNumber();
}
