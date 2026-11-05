namespace Budget.Shared.Outgoings;

public static class OutgoingCategoryRoutes
{
    /// <summary>GET: the household's categories. POST: add one.</summary>
    public static string Categories(Guid householdId) => $"/api/households/{householdId}/outgoing-categories";

    /// <summary>PUT: rename, hide or show. DELETE: delete an unused category.</summary>
    public static string Category(Guid householdId, Guid categoryId) => $"{Categories(householdId)}/{categoryId}";
}

/// <param name="OutgoingCount">How many outgoings use it; a category in use can't be deleted.</param>
public sealed record OutgoingCategorySummary(Guid Id, string Name, bool IsHidden, int OutgoingCount);

public sealed record AddCategoryRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed record UpdateCategoryRequest
{
    public string Name { get; init; } = string.Empty;

    public bool IsHidden { get; init; }
}

/// <summary>Shared by the form and the API.</summary>
public static class CategoryValidator
{
    public const int NameMaxLength = 50;

    /// <returns>Error messages keyed by "Name"; empty when valid.</returns>
    public static IReadOnlyDictionary<string, string[]> ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        string? error = trimmed.Length switch
        {
            0 => "Enter a name for the category.",
            > NameMaxLength => $"A category name can be at most {NameMaxLength} characters.",
            _ => null,
        };

        return error is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]> { ["Name"] = [error] };
    }
}
