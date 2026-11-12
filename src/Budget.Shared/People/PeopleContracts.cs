namespace Budget.Shared.People;

public static class PeopleRoutes
{
    /// <summary>GET: the household's people. POST: add a person.</summary>
    public static string People(Guid householdId) => $"/api/households/{householdId}/people";

    /// <summary>PUT: rename a person.</summary>
    public static string Person(Guid householdId, Guid personId) => $"{People(householdId)}/{personId}";

    /// <summary>GET (Admins): what removing the person would affect. POST <see cref="RemovePersonRequest"/>: remove them (BUD-112).</summary>
    public static string Removal(Guid householdId, Guid personId) => $"{Person(householdId, personId)}/removal";
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

/// <summary>What removing a person would affect (BUD-112, ADR-016).</summary>
/// <param name="LinkedToMember">They sign in as a current member, so they can't be removed until that member leaves.</param>
public sealed record PersonRemovalCheck(int IncomeCount, int OutgoingCount, int AccountCount, bool LinkedToMember);

/// <summary>
/// Remove a person who doesn't sign in (BUD-112, ADR-016), saying what happens to their items. A choice is only needed
/// for the kinds of item they have.
/// </summary>
public sealed record RemovePersonRequest
{
    /// <summary><see cref="RemovalChoices.Person"/> (to <see cref="IncomeTo"/>) or <see cref="RemovalChoices.Delete"/>. Income always belongs to a person.</summary>
    public string? Income { get; init; }

    public Guid? IncomeTo { get; init; }

    /// <summary><see cref="RemovalChoices.Household"/>, <see cref="RemovalChoices.Person"/> (to <see cref="OutgoingsTo"/>) or <see cref="RemovalChoices.Delete"/>.</summary>
    public string? Outgoings { get; init; }

    public Guid? OutgoingsTo { get; init; }

    /// <summary>Their accounts become the household's either way; this also archives them.</summary>
    public bool ArchiveAccounts { get; init; }
}

public static class RemovalChoices
{
    public const string Household = "Household";
    public const string Person = "Person";
    public const string Delete = "Delete";
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
