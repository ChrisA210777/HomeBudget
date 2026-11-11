using System.Globalization;

namespace Budget.Client;

/// <summary>
/// Names for the period a page shows (BUD-124, BUD-130). The year is left out when it's this year, so the name stays
/// short enough not to wrap on a phone: "November", "1 – 7 Nov", "29 Nov – 5 Dec"; otherwise "January 2027" or
/// "29 Dec 2026 – 4 Jan 2027".
/// </summary>
public static class PeriodNames
{
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    public static string Month(int year, int month, DateOnly today) =>
        new DateOnly(year, month, 1).ToString(year == today.Year ? "MMMM" : "MMMM yyyy", Display);

    /// <summary>A run of days, e.g. a week.</summary>
    public static string Days(DateOnly from, DateOnly to, DateOnly today) => Range(from, to, today, "%d", "d MMM");

    /// <summary>A run of months, e.g. the 12 months from November: "Nov 2026 – Oct 2027", or "Jan – Dec" this year.</summary>
    public static string Months(DateOnly from, DateOnly to, DateOnly today) => Range(from, to, today, null, "MMM");

    private static string Range(DateOnly from, DateOnly to, DateOnly today, string? sameMonth, string dayMonth)
    {
        var thisYear = from.Year == today.Year && to.Year == today.Year;
        var end = to.ToString(thisYear ? dayMonth : dayMonth + " yyyy", Display);
        var start = from.Year != to.Year ? from.ToString(dayMonth + " yyyy", Display)
            : from.Month == to.Month && sameMonth is not null ? from.ToString(sameMonth, Display)
            : from.ToString(dayMonth, Display);
        return $"{start} – {end}";
    }
}
