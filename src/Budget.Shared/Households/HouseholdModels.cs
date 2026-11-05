namespace Budget.Shared.Households;

public static class HouseholdRoutes
{
    /// <summary>All household-scoped API routes start with this (see Roles &amp; Permissions).</summary>
    public const string Base = "/api/households/{householdId:guid}";

    /// <summary>GET: the household's details and the caller's role in it. PUT (Admins): rename it.</summary>
    public static string Household(Guid householdId) => $"/api/households/{householdId}";
}

/// <summary>A household as seen by one of its members.</summary>
/// <param name="Role">The caller's role: Viewer, User or Admin.</param>
public sealed record HouseholdResponse(
    Guid Id,
    string Name,
    string CurrencyCode,
    string Role,
    bool IsOwner,
    int MemberCount);

/// <summary>Rename a household.</summary>
public sealed record RenameHouseholdRequest
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The name the caller was looking at. If someone else has renamed the household since,
    /// the rename is rejected with 409 rather than overwriting their change.
    /// </summary>
    public string? PreviousName { get; init; }
}

/// <summary>Shared by the form and the API.</summary>
public static class HouseholdValidator
{
    public const int NameMaxLength = 100;

    /// <returns>Error messages keyed by property name; empty when valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(RenameHouseholdRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim() ?? string.Empty;
        string? error = name.Length switch
        {
            0 => "Enter a name for the household.",
            > NameMaxLength => $"A household name can be at most {NameMaxLength} characters.",
            _ => null,
        };

        return error is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]> { [nameof(RenameHouseholdRequest.Name)] = [error] };
    }
}
