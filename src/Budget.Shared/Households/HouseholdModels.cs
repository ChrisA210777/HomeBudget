namespace Budget.Shared.Households;

public static class HouseholdRoutes
{
    /// <summary>All household-scoped API routes start with this (see Roles &amp; Permissions).</summary>
    public const string Base = "/api/households/{householdId:guid}";

    /// <summary>POST (any signed-in user): create another household, owned by the caller (BUD-21).</summary>
    public const string Households = "/api/households";

    /// <summary>GET: the household's details and the caller's role in it. PUT (Admins): rename it.</summary>
    public static string Household(Guid householdId) => $"/api/households/{householdId}";

    /// <summary>GET (any member): how far the household has got setting up, for the home page's prompts (BUD-68).</summary>
    public static string Setup(Guid householdId) => $"/api/households/{householdId}/setup";

    /// <summary>PUT (Admins): the household's settings, e.g. "Accounts and transfers" (BUD-91).</summary>
    public static string Settings(Guid householdId) => $"/api/households/{householdId}/settings";
}

/// <summary>How many of each thing the household has, whatever their dates.</summary>
public sealed record HouseholdSetupResponse(int Incomes, int Outgoings, int People);

/// <summary>A household as seen by one of its members.</summary>
/// <param name="Role">The caller's role: Viewer, User or Admin.</param>
public sealed record HouseholdResponse(
    Guid Id,
    string Name,
    string CurrencyCode,
    string Role,
    bool IsOwner,
    int MemberCount,
    bool TransfersEnabled = false);

/// <summary>The household's settings. Only Admins can change them.</summary>
public sealed record HouseholdSettingsRequest
{
    /// <summary>"Accounts and transfers" (ADR-014): transfers, the account check and calculated transfers.</summary>
    public bool TransfersEnabled { get; init; }
}

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

/// <summary>Create another household. The caller becomes its owner and it becomes their active household.</summary>
public sealed record CreateHouseholdRequest
{
    public const string DefaultName = "My Household";

    public string Name { get; init; } = DefaultName;
}

/// <summary>Shared by the form and the API.</summary>
public static class HouseholdValidator
{
    public const int NameMaxLength = 100;

    /// <returns>Error messages keyed by property name; empty when valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(RenameHouseholdRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValidateName(request.Name);
    }

    /// <returns>Error messages keyed by property name; empty when valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(CreateHouseholdRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValidateName(request.Name);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateName(string? requested)
    {
        var name = requested?.Trim() ?? string.Empty;
        string? error = name.Length switch
        {
            0 => "Enter a name for the household.",
            > NameMaxLength => $"A household name can be at most {NameMaxLength} characters.",
            _ => null,
        };

        return error is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]> { ["Name"] = [error] };
    }
}
