using System.Globalization;

namespace Budget.Client;

/// <summary>
/// Names for the period a page shows (BUD-124, BUD-130). They always include the year, this year too (BUD-135):
/// "November 2026", "1 – 7 Nov 2026", "29 Nov – 5 Dec 2026", "29 Dec 2026 – 4 Jan 2027".
/// </summary>
public static class PeriodNames
{
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    public static string Month(int year, int month) => new DateOnly(year, month, 1).ToString("MMMM yyyy", Display);

    /// <summary>A run of days, e.g. a week.</summary>
    public static string Days(DateOnly from, DateOnly to) => Range(from, to, "%d", "d MMM");

    /// <summary>A run of months, e.g. the 12 months from November: "Nov 2026 – Oct 2027", or "Jan – Dec 2027".</summary>
    public static string Months(DateOnly from, DateOnly to) => Range(from, to, null, "MMM");

    private static string Range(DateOnly from, DateOnly to, string? sameMonth, string dayMonth)
    {
        var end = to.ToString(dayMonth + " yyyy", Display);
        var start = from.Year != to.Year ? from.ToString(dayMonth + " yyyy", Display)
            : from.Month == to.Month && sameMonth is not null ? from.ToString(sameMonth, Display)
            : from.ToString(dayMonth, Display);
        return $"{start} – {end}";
    }
}
