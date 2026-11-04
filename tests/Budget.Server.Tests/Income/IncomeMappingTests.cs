using Budget.Domain.Income;
using Budget.Shared.Income;

namespace Budget.Server.Tests.Income;

/// <summary>The API enums are matched to the domain enums by name, so the names must stay in step.</summary>
public class IncomeMappingTests
{
    [Fact]
    public void Api_and_domain_enums_have_the_same_names()
    {
        AssertSameNames<IncomeStreamTypeDto, IncomeStreamType>();
        AssertSameNames<PayFrequencyDto, PayFrequency>();
        AssertSameNames<IncomeKindDto, IncomeKind>();
        AssertSameNames<AmountBasisDto, AmountBasis>();
    }

    private static void AssertSameNames<TDto, TDomain>()
        where TDto : struct, Enum
        where TDomain : struct, Enum
    {
        Assert.Equal(Enum.GetNames<TDomain>().Order(), Enum.GetNames<TDto>().Order());
    }
}
