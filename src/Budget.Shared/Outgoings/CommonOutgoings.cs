namespace Budget.Shared.Outgoings;

/// <summary>
/// A common UK outgoing with its usual settings, suggested as people type a name (BUD-54).
/// <see cref="Category"/> is one of the default category names; <see cref="Renews"/> says whether it
/// usually has a renewal date, which is then suggested a year (or <see cref="RenewalEveryMonths"/>)
/// after the first payment.
/// </summary>
public sealed record CommonOutgoing(
    string Name,
    OutgoingKindDto Kind,
    string Category,
    OutgoingPriorityDto Priority,
    int Every,
    PaymentUnitDto Unit,
    PaymentMethodDto? PaymentMethod = null,
    bool Renews = false,
    bool AutoRenews = false,
    AlertPeriodDto AlertPeriod = AlertPeriodDto.None,
    int RenewalEveryMonths = 12,
    string? ReferenceLabel = null,
    string? Website = null);

/// <summary>The built-in list of common outgoings (BUD-54). Part of the app, not per household.</summary>
public static class CommonOutgoings
{
    private const OutgoingKindDto Bill = OutgoingKindDto.Bill;
    private const OutgoingKindDto Savings = OutgoingKindDto.Savings;
    private const OutgoingKindDto Allowance = OutgoingKindDto.Allowance;
    private const OutgoingPriorityDto Essential = OutgoingPriorityDto.Essential;
    private const OutgoingPriorityDto Preferred = OutgoingPriorityDto.Preferred;
    private const OutgoingPriorityDto Optional = OutgoingPriorityDto.Optional;
    private const PaymentUnitDto Week = PaymentUnitDto.Week;
    private const PaymentUnitDto Month = PaymentUnitDto.Month;
    private const PaymentUnitDto Year = PaymentUnitDto.Year;
    private const PaymentMethodDto DirectDebit = PaymentMethodDto.DirectDebit;
    private const PaymentMethodDto StandingOrder = PaymentMethodDto.StandingOrder;
    private const PaymentMethodDto DebitCard = PaymentMethodDto.DebitCard;
    private const PaymentMethodDto CreditCard = PaymentMethodDto.CreditCard;
    private const PaymentMethodDto BankTransfer = PaymentMethodDto.BankTransfer;
    private const PaymentMethodDto Cash = PaymentMethodDto.Cash;
    private const AlertPeriodDto OneWeek = AlertPeriodDto.OneWeek;
    private const AlertPeriodDto TwoWeeks = AlertPeriodDto.TwoWeeks;
    private const AlertPeriodDto OneMonth = AlertPeriodDto.OneMonth;

    public static IReadOnlyList<CommonOutgoing> All { get; } =
    [
        // Housing
        new("Mortgage", Bill, "Housing", Essential, 1, Month, DirectDebit, ReferenceLabel: "Mortgage account number"),
        new("Mortgage overpayment", Bill, "Housing", Optional, 1, Month, StandingOrder, ReferenceLabel: "Mortgage account number"),
        new("Rent", Bill, "Housing", Essential, 1, Month, StandingOrder, ReferenceLabel: "Reference"),
        new("Council tax", Bill, "Housing", Essential, 1, Month, DirectDebit, ReferenceLabel: "Council tax account number"),
        new("Garden waste collection", Bill, "Housing", Preferred, 1, Year, DebitCard, Renews: true, AlertPeriod: OneMonth, ReferenceLabel: "Reference"),
        new("Ground rent / service charge", Bill, "Housing", Essential, 1, Year, BankTransfer, ReferenceLabel: "Reference"),

        // Utilities
        new("Electricity", Bill, "Utilities", Essential, 1, Month, DirectDebit, ReferenceLabel: "Account number"),
        new("Gas", Bill, "Utilities", Essential, 1, Month, DirectDebit, ReferenceLabel: "Account number"),
        new("Water", Bill, "Utilities", Essential, 1, Month, DirectDebit, ReferenceLabel: "Customer number"),

        // Phone, internet & TV
        new("Broadband / internet", Bill, "Phone & internet", Preferred, 1, Month, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, RenewalEveryMonths: 18, ReferenceLabel: "Account number"),
        new("Mobile phone", Bill, "Phone & internet", Preferred, 1, Month, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, RenewalEveryMonths: 24, ReferenceLabel: "Account number"),
        new("TV Licence", Bill, "Subscriptions & TV", Essential, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "TV Licence number", Website: "https://www.tvlicensing.co.uk"),

        // Insurance
        new("Buildings insurance", Bill, "Insurance", Essential, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Policy number"),
        new("Home contents insurance", Bill, "Insurance", Preferred, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Policy number"),
        new("Life insurance", Bill, "Insurance", Essential, 1, Month, DirectDebit, ReferenceLabel: "Policy number"),
        new("Pet insurance", Bill, "Insurance", Preferred, 1, Month, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Policy number"),
        new("Travel insurance", Bill, "Insurance", Preferred, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Policy number"),
        new("Car insurance", Bill, "Insurance", Essential, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Policy number"),

        // Pets
        new("Cat care plan", Bill, "Pets", Preferred, 1, Month, DirectDebit, ReferenceLabel: "Membership number"),
        new("Dog care plan", Bill, "Pets", Preferred, 1, Month, DirectDebit, ReferenceLabel: "Membership number"),

        // Car & travel
        new("Breakdown cover", Bill, "Car & travel", Preferred, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Membership number"),
        new("MOT", Bill, "Car & travel", Essential, 1, Year, DebitCard, Renews: true, AlertPeriod: OneMonth, ReferenceLabel: "Registration number"),
        new("Car service", Bill, "Car & travel", Preferred, 1, Year, DebitCard, Renews: true, AlertPeriod: TwoWeeks, ReferenceLabel: "Registration number"),
        new("Car tax", Bill, "Car & travel", Essential, 1, Month, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Registration number", Website: "https://www.gov.uk/vehicle-tax"),
        new("Car parts", Allowance, "Car & travel", Optional, 1, Month),
        new("Fuel", Allowance, "Car & travel", Essential, 1, Week),
        new("Work travel", Allowance, "Car & travel", Essential, 1, Month),

        // Software
        new("Microsoft 365", Bill, "Subscriptions & TV", Optional, 1, Year, CreditCard, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, Website: "https://www.microsoft.com/microsoft-365"),
        new("Antivirus subscription", Bill, "Subscriptions & TV", Preferred, 1, Year, CreditCard, Renews: true, AutoRenews: true, AlertPeriod: OneMonth),
        new("Password manager subscription", Bill, "Subscriptions & TV", Preferred, 1, Year, CreditCard, Renews: true, AutoRenews: true, AlertPeriod: OneMonth),

        // Subscriptions
        new("Amazon Prime", Bill, "Subscriptions & TV", Optional, 1, Month, CreditCard, Website: "https://www.amazon.co.uk/prime"),
        new("Netflix", Bill, "Subscriptions & TV", Optional, 1, Month, CreditCard, Website: "https://www.netflix.com"),
        new("Disney+", Bill, "Subscriptions & TV", Optional, 1, Month, CreditCard, Website: "https://www.disneyplus.com"),
        new("Spotify", Bill, "Subscriptions & TV", Optional, 1, Month, CreditCard, Website: "https://www.spotify.com"),
        new("Apple One", Bill, "Subscriptions & TV", Optional, 1, Month, CreditCard, Website: "https://www.apple.com/apple-one"),
        new("YouTube Premium", Bill, "Subscriptions & TV", Optional, 1, Month, CreditCard, Website: "https://www.youtube.com/premium"),
        new("Duolingo", Bill, "Subscriptions & TV", Optional, 1, Year, CreditCard, Renews: true, AutoRenews: true, AlertPeriod: OneWeek, Website: "https://www.duolingo.com"),
        new("Gym membership", Bill, "Leisure & memberships", Optional, 1, Month, DirectDebit, ReferenceLabel: "Membership number"),

        // Health
        new("NHS prescription prepayment certificate", Bill, "Health & personal care", Essential, 1, Year, DebitCard, Renews: true, AlertPeriod: OneMonth, ReferenceLabel: "Certificate number", Website: "https://www.nhsbsa.nhs.uk/help-nhs-prescription-costs/prescription-prepayment-certificates-ppcs"),
        new("Glasses / eye test", Bill, "Health & personal care", Preferred, 2, Year, DebitCard, Renews: true, AlertPeriod: OneMonth, RenewalEveryMonths: 24),
        new("Dentist", Bill, "Health & personal care", Preferred, 6, Month, DebitCard),
        new("Haircut", Allowance, "Health & personal care", Optional, 1, Month),

        // Home
        new("Boiler service", Bill, "Home maintenance", Preferred, 1, Year, DebitCard, Renews: true, AlertPeriod: OneMonth),
        new("Chimney sweep", Bill, "Home maintenance", Preferred, 1, Year, BankTransfer, Renews: true, AlertPeriod: OneMonth),
        new("Window cleaner", Bill, "Home maintenance", Optional, 4, Week, Cash),
        new("Gardener", Bill, "Home maintenance", Optional, 2, Week, BankTransfer),

        // Leisure
        new("English Heritage membership", Bill, "Leisure & memberships", Optional, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Membership number", Website: "https://www.english-heritage.org.uk"),
        new("National Trust membership", Bill, "Leisure & memberships", Optional, 1, Year, DirectDebit, Renews: true, AutoRenews: true, AlertPeriod: OneMonth, ReferenceLabel: "Membership number", Website: "https://www.nationaltrust.org.uk"),

        // Giving
        new("Charity donation", Bill, "Gifts & giving", Optional, 1, Month, DirectDebit),
        new("Christmas presents", Savings, "Gifts & giving", Preferred, 1, Month, StandingOrder),
        new("Birthday presents", Savings, "Gifts & giving", Preferred, 1, Month, StandingOrder),

        // Food & household
        new("Food shopping", Allowance, "Food & household", Essential, 1, Week),
        new("Milk delivery", Bill, "Food & household", Optional, 1, Week, DebitCard),

        // Children
        new("School lunches", Bill, "Children", Essential, 1, Month, DebitCard),

        // Savings
        new("Savings", Savings, "Savings & set-asides", Preferred, 1, Month, StandingOrder),
        new("Holiday savings", Savings, "Savings & set-asides", Preferred, 1, Month, StandingOrder),
        new("Children's savings", Savings, "Savings & set-asides", Preferred, 1, Month, StandingOrder),
        new("House savings", Savings, "Savings & set-asides", Preferred, 1, Month, StandingOrder),
        new("Replacement fund (depreciation)", Savings, "Savings & set-asides", Preferred, 1, Month, StandingOrder),
        new("New phones", Savings, "Savings & set-asides", Optional, 1, Month, StandingOrder),

        // Spending and fees
        new("Discretionary spending", Allowance, "Discretionary spending", Optional, 1, Month),
        new("Bank account fee", Bill, "Banking & fees", Essential, 1, Month, DirectDebit),
    ];

    /// <summary>The common outgoing with exactly this name, ignoring case and surrounding spaces.</summary>
    public static CommonOutgoing? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : All.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Suggestions for what's been typed: names where any word starts with it (e.g. "insur" finds
    /// Car insurance and Pet insurance), or that contain it, in list order.
    /// </summary>
    public static IReadOnlyList<CommonOutgoing> Search(string? typed, int max = 10)
    {
        var text = typed?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return [];
        }

        return All
            .Where(c => c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || c.Name.Split([' ', '/', '(', ')', '-'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(word => word.StartsWith(text, StringComparison.OrdinalIgnoreCase)))
            .Take(max)
            .ToList();
    }
}
