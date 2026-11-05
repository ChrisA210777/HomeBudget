namespace Budget.Shared.People;

public static class PeopleRoutes
{
    /// <summary>GET: the household's people. POST: add a person.</summary>
    public static string People(Guid householdId) => $"/api/households/{householdId}/people";

    /// <summary>PUT: rename a person.</summary>
    public static string Person(Guid householdId, Guid personId) => $"{People(householdId)}/{personId}";
}

/// <summary>A person in the household.</summary>
/// <param name="LinkedUserName">The display name of the login linked to this person, if any.</param>
/// <param name="IsYou">Whether this person is linked to the caller's own login.</param>
public sealed record PersonSummary(Guid Id, string DisplayName, Guid? LinkedUserId, string? LinkedUserName, bool IsYou);

/// <summary>Add or rename a person.</summary>
public sealed record SavePersonRequest
{
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>Shared by the form and the API.</summary>
public static class PersonValidator
{
    public const int DisplayNameMaxLength = 100;

    /// <returns>Error messages keyed by property name; empty when valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(SavePersonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Error("Enter a name.");
        }

        if (name.Length > DisplayNameMaxLength)
        {
            return Error($"A name can be at most {DisplayNameMaxLength} characters.");
        }

        return new Dictionary<string, string[]>();
    }

    private static Dictionary<string, string[]> Error(string message) =>
        new() { [nameof(SavePersonRequest.DisplayName)] = [message] };
}
