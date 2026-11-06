using Budget.Shared.Outgoings;

namespace Budget.Client.Tests.Outgoings;

public sealed class ReferenceLabelSuggestionTests
{
    [Fact]
    public void The_households_own_names_come_first_then_common_ones_without_duplicates()
    {
        var suggestions = OutgoingValidator.SuggestReferenceLabels(["TV Licence number", "policy number"], typed: null);

        Assert.Equal("TV Licence number", suggestions[0]);
        Assert.Equal("policy number", suggestions[1]);
        Assert.DoesNotContain("Policy number", suggestions);
        Assert.Contains("Membership number", suggestions);
        Assert.Equal(suggestions.Count, suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("numb", "Policy number|Account number|Customer number|Membership number|Registration number")]
    [InlineData("BOOK", "Booking reference")]
    [InlineData("zzz", "")]
    public void Typing_matches_anywhere_ignoring_case(string typed, string expected)
    {
        Assert.Equal(expected, string.Join('|', OutgoingValidator.SuggestReferenceLabels([], typed)));
    }
}
