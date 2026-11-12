using System.Globalization;
using System.Text.RegularExpressions;
using Budget.Shared.Activity;
using Budget.Shared.Income;

namespace Budget.Client.Activity;

/// <summary>How recent activity reads (BUD-139): the sentence with its amounts, when, and where it links to.</summary>
public static partial class ActivityFormat
{
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    /// <summary>"Chris paused Netflix from 1 Dec 2026", with <c>{{480.00}}</c> shown as money.</summary>
    public static string Sentence(ActivityItem item, string currency)
    {
        ArgumentNullException.ThrowIfNull(item);
        var summary = Amount().Replace(item.Summary, m => Money.Format(decimal.Parse(m.Groups[1].Value, Display), currency));
        return $"{item.Who} {summary}";
    }

    /// <summary>"10:42" today, "Yesterday 18:05", or "Mon 9 Nov 18:05" (local time).</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        var time = local.ToString("HH:mm", Display);
        return day == today ? time
            : day == today.AddDays(-1) ? $"Yesterday {time}"
            : day.Year == today.Year ? $"{local.ToString("ddd d MMM", Display)} {time}"
            : $"{local.ToString("d MMM yyyy", Display)} {time}";
    }

    /// <summary>Where the item is, while it exists.</summary>
    public static string? Link(ActivityItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.SubjectId is not { } id || !item.Exists)
        {
            return item.Area switch
            {
                ActivityAreaDto.Members => "/members",
                _ => null,
            };
        }

        return item.Area switch
        {
            ActivityAreaDto.Income => $"/income/{id}",
            ActivityAreaDto.Outgoings => $"/outgoings/{id}",
            ActivityAreaDto.Transfers => $"/transfers/{id}",
            ActivityAreaDto.Accounts => "/accounts",
            ActivityAreaDto.Categories => "/outgoings/categories",
            ActivityAreaDto.People => "/people",
            _ => null,
        };
    }

    [GeneratedRegex(@"\{\{(-?\d+(?:\.\d+)?)\}\}")]
    private static partial Regex Amount();
}
