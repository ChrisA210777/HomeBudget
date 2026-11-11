using System.Globalization;

namespace Budget.Client;

/// <summary>
/// Names for the period a page shows (BUD-124, BUD-130). They always include the year, this year too (BUD-135):
/// "November 2026" and "Nov 2026 – Oct 2027". A run of days, e.g. a week, stays short enough for a phone: a two-digit
/// year, given once at the end even across New Year, as the end year makes the start's clear: "1 – 7 Nov 26",
/// "29 Nov – 5 Dec 26", "29 Dec – 4 Jan 27".
/// </summary>
public static class PeriodNames
{
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    public static string Month(int year, int month) => new DateOnly(year, month, 1).ToString("MMMM yyyy", Display);

    /// <summary>A run of days, e.g. a week, with a two-digit year at the end only.</summary>
    public static string Days(DateOnly from, DateOnly to) => Range(from, to, "%d", "d MMM", "yy", yearOnStart: false);

    /// <summary>A run of months, e.g. the 12 months from November: "Nov 2026 – Oct 2027", or "Jan – Dec 2027".</summary>
    public static string Months(DateOnly from, DateOnly to) => Range(from, to, null, "MMM", "yyyy", yearOnStart: true);

    /// <param name="yearOnStart">Whether the start also has its year when it's in a different year from the end.</param>
    private static string Range(DateOnly from, DateOnly to, string? sameMonth, string dayMonth, string year, bool yearOnStart)
    {
        var end = to.ToString($"{dayMonth} {year}", Display);
        var start = from.Year != to.Year ? from.ToString(yearOnStart ? $"{dayMonth} {year}" : dayMonth, Display)
            : from.Month == to.Month && sameMonth is not null ? from.ToString(sameMonth, Display)
            : from.ToString(dayMonth, Display);
        return $"{start} – {end}";
    }
}
