namespace Budget.Shared.Income;

/// <summary>
/// Validation for <see cref="SaveEmploymentIncomeRequest"/>, shared by the form (so people see
/// problems as they type) and the API (which is the real check).
/// </summary>
public static class EmploymentIncomeValidator
{
    public const int NameMaxLength = 100;
    public const int EmployerNameMaxLength = 100;
    public const decimal MaxAmount = 100_000_000m;

    /// <returns>Error messages keyed by property name; empty when the request is valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(SaveEmploymentIncomeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>();
        void Add(string property, string message)
        {
            if (!errors.TryGetValue(property, out var list))
            {
                errors[property] = list = [];
            }

            list.Add(message);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            Add(nameof(request.Name), "Enter a name for this income, e.g. \"Sam – Acme Ltd\".");
        }
        else if (name.Length > NameMaxLength)
        {
            Add(nameof(request.Name), $"The name can be at most {NameMaxLength} characters.");
        }

        if (request.EmployerName?.Trim().Length > EmployerNameMaxLength)
        {
            Add(nameof(request.EmployerName), $"The employer name can be at most {EmployerNameMaxLength} characters.");
        }

        if (!Enum.IsDefined(request.PayFrequency))
        {
            Add(nameof(request.PayFrequency), "Choose how often you're paid.");
        }

        switch (request.Kind)
        {
            case IncomeKindDto.Fixed:
                CheckAmount(request.GrossAmount, nameof(request.GrossAmount), "gross pay", Add);
                if (request.AmountBasis is not { } basis || !Enum.IsDefined(basis))
                {
                    Add(nameof(request.AmountBasis), "Choose what period the amount is for.");
                }

                break;

            case IncomeKindDto.Variable:
                var lowestOk = CheckAmount(request.LowestGross, nameof(request.LowestGross), "lowest gross pay", Add);
                var averageOk = CheckAmount(request.AverageGross, nameof(request.AverageGross), "average gross pay", Add);
                var highestOk = CheckAmount(request.HighestGross, nameof(request.HighestGross), "highest gross pay", Add);
                if (lowestOk && averageOk && request.LowestGross > request.AverageGross)
                {
                    Add(nameof(request.LowestGross), "The lowest can't be more than the average.");
                }

                if (averageOk && highestOk && request.AverageGross > request.HighestGross)
                {
                    Add(nameof(request.HighestGross), "The highest can't be less than the average.");
                }

                break;

            default:
                Add(nameof(request.Kind), "Choose whether pay is fixed or variable.");
                break;
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    private static bool CheckAmount(decimal? amount, string property, string label, Action<string, string> add)
    {
        if (amount is not { } value)
        {
            add(property, $"Enter the {label}.");
            return false;
        }

        if (value <= 0)
        {
            add(property, $"The {label} must be more than zero.");
            return false;
        }

        if (value > MaxAmount)
        {
            add(property, $"The {label} looks too large. Check for extra zeros.");
            return false;
        }

        if (decimal.Round(value, 2) != value)
        {
            add(property, "Use pounds and pence, with at most 2 decimal places.");
            return false;
        }

        return true;
    }
}
