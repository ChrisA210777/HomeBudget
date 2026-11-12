using System.Collections.Concurrent;
using Budget.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Budget.Server.Tests.Hosting;

/// <summary>Values marked as personal or secret are erased from logs (BUD-146).</summary>
public partial class LogRedactionTests
{
    [Fact]
    public void Values_marked_personal_or_secret_are_erased_and_others_kept()
    {
        var entries = new ConcurrentQueue<string>();
        using var services = new ServiceCollection()
            .AddLogging(logging => logging.EnableRedaction().AddProvider(new Capture(entries)).SetMinimumLevel(LogLevel.Information))
            .AddBudgetRedaction()
            .BuildServiceProvider();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
        var userId = Guid.NewGuid();

        LogInvited(logger, userId, "qzvk@example.com", "CfDJ8-secret-token");

        var entry = Assert.Single(entries);
        Assert.Contains(userId.ToString(), entry, StringComparison.Ordinal);
        Assert.DoesNotContain("qzvk@example.com", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("CfDJ8-secret-token", entry, StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} invited {Email} with {Token}")]
    private static partial void LogInvited(ILogger logger, Guid userId, [PersonalData] string email, [SecretData] string token);

    private sealed class Capture(ConcurrentQueue<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // The message and the structured values, which are both sent to Application Insights.
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                entries.Enqueue($"{formatter(state, exception)} {values}");
            }
        }
    }
}
