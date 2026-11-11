namespace Budget.Client.Tests;

/// <summary>Period names leave out the year when it's this year (BUD-124, BUD-130).</summary>
public sealed class PeriodNamesTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    [Theory]
    [InlineData(2026, 11, "November")]
    [InlineData(2027, 1, "January 2027")]
    [InlineData(2025, 12, "December 2025")]
    public void Months(int year, int month, string expected) => Assert.Equal(expected, PeriodNames.Month(year, month, Today));

    [Theory]
    [InlineData("2026-11-01", "2026-11-07", "1 – 7 Nov")]
    [InlineData("2026-11-29", "2026-12-05", "29 Nov – 5 Dec")]
    [InlineData("2026-12-27", "2027-01-02", "27 Dec 2026 – 2 Jan 2027")]
    [InlineData("2027-02-07", "2027-02-13", "7 – 13 Feb 2027")]
    public void Days(string from, string to, string expected) =>
        Assert.Equal(expected, PeriodNames.Days(DateOnly.Parse(from), DateOnly.Parse(to), Today));

    [Theory]
    [InlineData("2026-11-01", "2027-10-01", "Nov 2026 – Oct 2027")]
    [InlineData("2026-01-01", "2026-12-01", "Jan – Dec")]
    public void Runs_of_months(string from, string to, string expected) =>
        Assert.Equal(expected, PeriodNames.Months(DateOnly.Parse(from), DateOnly.Parse(to), Today));
}
