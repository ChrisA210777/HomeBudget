using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Budget.Server.Hosting;

/// <summary>
/// Keeps personal data and secrets out of what's sent to Application Insights (BUD-146). The app's own log messages
/// only hold ids, but some things reach telemetry by other routes:
/// <list type="bullet">
/// <item>Request addresses: invitation links carry their token in the path (<c>/invite/{token}</c>,
/// <c>/api/invitations/{token}</c>), importing as a new household can carry its name (<c>?name=</c>), and sign-in carries
/// where to go next (<c>?returnUrl=</c>), which can be an invitation link.</item>
/// <item>Exception messages from SQL Server, which can quote the values involved, e.g. "The duplicate key value is
/// (…)".</item>
/// </list>
/// </summary>
public static partial class TelemetryRedaction
{
    public const string Redacted = "(redacted)";

    /// <summary>Query parameters whose values can hold a name, free text or a link with a token.</summary>
    private static readonly HashSet<string> RedactedQueryKeys = new(StringComparer.OrdinalIgnoreCase) { "name", "returnUrl" };

    /// <summary>The path, with any invitation token replaced.</summary>
    public static string RedactPath(string path) => InvitationToken().Replace(path, "$1{token}");

    /// <summary>The query string (with or without its "?"), with the values that can hold personal data replaced.</summary>
    public static string RedactQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return query;
        }

        var prefix = query[0] == '?' ? "?" : string.Empty;
        var parts = query[prefix.Length..].Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var equals = parts[i].IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && RedactedQueryKeys.Contains(Uri.UnescapeDataString(parts[i][..equals])))
            {
                parts[i] = parts[i][..(equals + 1)] + Redacted;
            }
        }

        return prefix + string.Join('&', parts);
    }

    /// <summary>A full or relative address, with its path and query redacted.</summary>
    public static string RedactUrl(string url)
    {
        var question = url.IndexOf('?', StringComparison.Ordinal);
        return question < 0
            ? RedactPath(url)
            : RedactPath(url[..question]) + RedactQuery(url[question..]);
    }

    /// <summary>A message, with values quoted by SQL Server replaced.</summary>
    public static string RedactMessage(string message)
    {
        var redacted = DuplicateKeyValue().Replace(message, "$1(" + Redacted + ")");
        redacted = TruncatedValue().Replace(redacted, "$1'" + Redacted + "'");
        return ConvertedValue().Replace(redacted, "$1'" + Redacted + "'");
    }

    /// <summary>
    /// The exception unchanged, or, if any message in it or its inner exceptions quotes a value, a copy with those
    /// messages redacted that keeps each exception's type name and stack trace.
    /// </summary>
    public static Exception? RedactException(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (RedactMessage(e.Message) != e.Message)
            {
                return new RedactedException(exception!);
            }
        }

        return exception;
    }

    [GeneratedRegex("(/invite/|/api/invitations/)[^/?#]+", RegexOptions.IgnoreCase)]
    private static partial Regex InvitationToken();

    [GeneratedRegex(@"(duplicate key value is )\(.*?\)(?=\.|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DuplicateKeyValue();

    [GeneratedRegex(@"(Truncated value: )'.*?'(?=\.|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TruncatedValue();

    [GeneratedRegex(@"(converting the \w+ value )'.*?'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ConvertedValue();
}

/// <summary>Stands in for an exception whose message quoted a value (BUD-146): the same type name, message without the value, and stack trace.</summary>
public sealed class RedactedException : Exception
{
    private readonly string? _stackTrace;

    public RedactedException(Exception original)
        : base(
            $"{original?.GetType().FullName}: {TelemetryRedaction.RedactMessage(original?.Message ?? string.Empty)}",
            original?.InnerException is { } inner ? new RedactedException(inner) : null)
    {
        _stackTrace = original?.StackTrace;
    }

    public override string? StackTrace => _stackTrace;
}

/// <summary>Redacts request addresses on traces before they're exported (BUD-146).</summary>
public sealed class RedactingActivityProcessor : BaseProcessor<Activity>
{
    private static readonly string[] UrlTags = ["url.full", "url.path", "url.query", "http.url", "http.target"];

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        foreach (var tag in UrlTags)
        {
            if (data.GetTagItem(tag) is string value)
            {
                var redacted = tag == "url.query" ? TelemetryRedaction.RedactQuery(value) : TelemetryRedaction.RedactUrl(value);
                if (!string.Equals(redacted, value, StringComparison.Ordinal))
                {
                    data.SetTag(tag, redacted);
                }
            }
        }

        // A request that matched no route is named after its path.
        var name = TelemetryRedaction.RedactUrl(data.DisplayName);
        if (!string.Equals(name, data.DisplayName, StringComparison.Ordinal))
        {
            data.DisplayName = name;
        }
    }
}

/// <summary>Redacts values quoted in exception messages on logs before they're exported (BUD-146).</summary>
public sealed class RedactingLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        data.Exception = TelemetryRedaction.RedactException(data.Exception);
        if (data.FormattedMessage is { } message)
        {
            data.FormattedMessage = TelemetryRedaction.RedactMessage(message);
        }
    }
}
