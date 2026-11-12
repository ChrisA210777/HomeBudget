using Budget.Server.Hosting;

namespace Budget.Server.Tests.Hosting;

/// <summary>What's redacted before telemetry is sent to Application Insights (BUD-146).</summary>
public class TelemetryRedactionTests
{
    [Theory]
    [InlineData("/invite/CfDJ8abc-_xyz", "/invite/{token}")]
    [InlineData("/api/invitations/CfDJ8abc-_xyz", "/api/invitations/{token}")]
    [InlineData("/api/invitations/CfDJ8abc-_xyz/accept", "/api/invitations/{token}/accept")]
    [InlineData("https://budget.example/invite/abc?x=1", "https://budget.example/invite/{token}?x=1")]
    [InlineData("/api/households/0192f0a0-0000-7000-8000-000000000001/outgoings", "/api/households/0192f0a0-0000-7000-8000-000000000001/outgoings")]
    public void Invitation_tokens_are_taken_out_of_addresses(string url, string expected) =>
        Assert.Equal(expected, TelemetryRedaction.RedactUrl(url));

    [Theory]
    [InlineData("?name=The%20Smiths&me=0192f0a0-0000-7000-8000-000000000001", "?name=(redacted)&me=0192f0a0-0000-7000-8000-000000000001")]
    [InlineData("returnUrl=%2Finvite%2Fabc", "returnUrl=(redacted)")]
    [InlineData("?ReturnUrl=/invite/abc&x=1", "?ReturnUrl=(redacted)&x=1")]
    [InlineData("?history=true&audit=false", "?history=true&audit=false")]
    [InlineData("", "")]
    public void Names_and_return_addresses_are_taken_out_of_query_strings(string query, string expected) =>
        Assert.Equal(expected, TelemetryRedaction.RedactQuery(query));

    [Theory]
    [InlineData(
        "Cannot insert duplicate key row in object 'dbo.People' with unique index 'IX_People'. The duplicate key value is (0192f0a0-0000-7000-8000-000000000001, Jane Smith). The statement has been terminated.",
        "Cannot insert duplicate key row in object 'dbo.People' with unique index 'IX_People'. The duplicate key value is ((redacted)). The statement has been terminated.")]
    [InlineData(
        "String or binary data would be truncated in table 'Budget.dbo.People', column 'DisplayName'. Truncated value: 'Jane Smith the'.",
        "String or binary data would be truncated in table 'Budget.dbo.People', column 'DisplayName'. Truncated value: '(redacted)'.")]
    [InlineData(
        "Conversion failed when converting the nvarchar value 'jane@example.com' to data type int.",
        "Conversion failed when converting the nvarchar value '(redacted)' to data type int.")]
    [InlineData("Object reference not set to an instance of an object.", "Object reference not set to an instance of an object.")]
    public void Values_quoted_by_SQL_Server_are_taken_out_of_messages(string message, string expected) =>
        Assert.Equal(expected, TelemetryRedaction.RedactMessage(message));

    [Fact]
    public void An_exception_quoting_a_value_is_replaced_keeping_its_type_and_inner_exceptions()
    {
        var inner = Thrown(new InvalidOperationException("The duplicate key value is (jane@example.com)."));
        var outer = Thrown(new InvalidOperationException("An error occurred while saving the entity changes.", inner));

        var redacted = TelemetryRedaction.RedactException(outer);

        Assert.IsType<RedactedException>(redacted);
        Assert.Equal("System.InvalidOperationException: An error occurred while saving the entity changes.", redacted!.Message);
        Assert.Equal(outer.StackTrace, redacted.StackTrace);
        Assert.Equal("System.InvalidOperationException: The duplicate key value is ((redacted)).", redacted.InnerException!.Message);
        Assert.DoesNotContain("jane@example.com", redacted.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Other_exceptions_are_left_alone()
    {
        var exception = new InvalidOperationException("Something went wrong.");

        Assert.Same(exception, TelemetryRedaction.RedactException(exception));
    }

    [Fact]
    public void The_trace_processor_redacts_request_addresses()
    {
        using var activity = new System.Diagnostics.Activity("GET /invite/abc");
        activity.SetTag("url.path", "/api/invitations/CfDJ8secret/accept");
        activity.SetTag("url.query", "?returnUrl=%2Finvite%2FCfDJ8secret");
        activity.SetTag("url.full", "https://budget.example/invite/CfDJ8secret");
        activity.SetTag("http.route", "/api/invitations/{token}/accept");

        using var processor = new RedactingActivityProcessor();
        processor.OnEnd(activity);

        Assert.Equal("/api/invitations/{token}/accept", activity.GetTagItem("url.path"));
        Assert.Equal("?returnUrl=(redacted)", activity.GetTagItem("url.query"));
        Assert.Equal("https://budget.example/invite/{token}", activity.GetTagItem("url.full"));
        Assert.Equal("/api/invitations/{token}/accept", activity.GetTagItem("http.route"));
        Assert.Equal("GET /invite/{token}", activity.DisplayName);
    }

    private static T Thrown<T>(T exception)
        where T : Exception
    {
        try
        {
            throw exception;
        }
        catch (T caught)
        {
            return caught;
        }
    }
}
