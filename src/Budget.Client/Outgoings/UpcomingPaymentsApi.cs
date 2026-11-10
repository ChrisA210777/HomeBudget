using System.Net.Http.Json;
using Budget.Shared.Outgoings;

namespace Budget.Client.Outgoings;

/// <summary>The payments actually due between two dates (BUD-82).</summary>
public interface IUpcomingPaymentsApi
{
    Task<UpcomingPaymentsResponse> GetAsync(Guid householdId, DateOnly from, DateOnly to);
}

public sealed class UpcomingPaymentsApi(HttpClient http) : IUpcomingPaymentsApi
{
    public async Task<UpcomingPaymentsResponse> GetAsync(Guid householdId, DateOnly from, DateOnly to) =>
        (await http.GetFromJsonAsync<UpcomingPaymentsResponse>(new Uri(OutgoingRoutes.Payments(householdId, from, to), UriKind.Relative)))!;
}

/// <summary>Works out the days a week or month view covers (BUD-82).</summary>
public static class PaymentPeriods
{
    /// <summary>The week containing <paramref name="date"/>, starting on <paramref name="firstDay"/>.</summary>
    public static (DateOnly From, DateOnly To) WeekOf(DateOnly date, DayOfWeek firstDay)
    {
        var start = date.AddDays(-(((int)date.DayOfWeek - (int)firstDay + 7) % 7));
        return (start, start.AddDays(6));
    }

    /// <summary>How often a payment is made, e.g. "Monthly", "Every 4 weeks".</summary>
    public static string Frequency(int every, Budget.Shared.Outgoings.PaymentUnitDto unit)
    {
        var (adjective, noun) = unit switch
        {
            Budget.Shared.Outgoings.PaymentUnitDto.Week => ("Weekly", "week"),
            Budget.Shared.Outgoings.PaymentUnitDto.Month => ("Monthly", "month"),
            _ => ("Yearly", "year"),
        };
        return every == 1 ? adjective : $"Every {every} {noun}s";
    }

    /// <summary>The calendar month containing <paramref name="date"/>.</summary>
    public static (DateOnly From, DateOnly To) MonthOf(DateOnly date)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
