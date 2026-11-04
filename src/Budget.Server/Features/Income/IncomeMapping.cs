using Budget.Domain.Income;
using Budget.Shared.Income;

namespace Budget.Server.Features.Income;

/// <summary>Converts between the API contracts and the domain model. Enums are matched by name.</summary>
public static class IncomeMapping
{
    public static PayFrequency ToDomain(this PayFrequencyDto value) => Enum.Parse<PayFrequency>(value.ToString());

    public static AmountBasis ToDomain(this AmountBasisDto value) => Enum.Parse<AmountBasis>(value.ToString());

    public static PayFrequencyDto ToDto(this PayFrequency value) => Enum.Parse<PayFrequencyDto>(value.ToString());

    public static AmountBasisDto ToDto(this AmountBasis value) => Enum.Parse<AmountBasisDto>(value.ToString());

    public static IncomeKindDto ToDto(this IncomeKind value) => Enum.Parse<IncomeKindDto>(value.ToString());

    public static IncomeStreamTypeDto ToDto(this IncomeStreamType value) => Enum.Parse<IncomeStreamTypeDto>(value.ToString());

    /// <summary>The pay described by a request that has already passed validation.</summary>
    public static EmploymentPay ToPay(this SaveEmploymentIncomeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            IncomeKindDto.Fixed => new FixedPay(request.GrossAmount!.Value, request.AmountBasis!.Value.ToDomain()),
            IncomeKindDto.Variable => new VariablePay(request.LowestGross!.Value, request.AverageGross!.Value, request.HighestGross!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown income kind."),
        };
    }

    public static IncomeStreamSummary ToSummary(this IncomeStream stream, string? memberName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var employment = stream as EmploymentIncome
            ?? throw new NotSupportedException($"Income stream type {stream.Type} isn't supported yet.");
        var estimate = stream.Estimate();

        return new IncomeStreamSummary(
            stream.Id,
            stream.Type.ToDto(),
            stream.Name,
            employment.EmployerName,
            stream.MemberUserId,
            memberName,
            employment.PayFrequency.ToDto(),
            employment.Kind.ToDto(),
            stream.IsActive,
            PayCalculator.RoundForDisplay(estimate.LowPerPay),
            PayCalculator.RoundForDisplay(estimate.AveragePerPay),
            PayCalculator.RoundForDisplay(estimate.HighPerPay),
            PayCalculator.RoundForDisplay(estimate.LowAnnual),
            PayCalculator.RoundForDisplay(estimate.AverageAnnual),
            PayCalculator.RoundForDisplay(estimate.HighAnnual));
    }

    public static EmploymentIncomeDetails ToDetails(this EmploymentIncome income, byte[] version)
    {
        ArgumentNullException.ThrowIfNull(income);

        return new EmploymentIncomeDetails(
            income.Id,
            income.Name,
            income.EmployerName,
            income.MemberUserId,
            income.PayFrequency.ToDto(),
            income.Kind.ToDto(),
            income.GrossAmount,
            income.AmountBasis?.ToDto(),
            income.LowestGross,
            income.AverageGross,
            income.HighestGross,
            income.IsActive,
            Convert.ToBase64String(version));
    }
}
