namespace Budget.Shared.Outgoings;

public static class OutgoingCategoryRoutes
{
    /// <summary>GET: the household's categories. POST: add one.</summary>
    public static string Categories(Guid householdId) => $"/api/households/{householdId}/outgoing-categories";

    /// <summary>PUT: rename, hide or show. DELETE: delete an unused category.</summary>
    public static string Category(Guid householdId, Guid categoryId) => $"{Categories(householdId)}/{categoryId}";
}

/// <param name="OutgoingCount">How many outgoings use it; a category in use can't be deleted.</param>
/// <param name="Icon">A key from <see cref="CategoryIcons"/>.</param>
public sealed record OutgoingCategorySummary(Guid Id, string Name, bool IsHidden, int OutgoingCount, string Icon = CategoryIcons.Default);

public sealed record AddCategoryRequest
{
    public string Name { get; init; } = string.Empty;

    /// <summary>A key from <see cref="CategoryIcons"/>; <c>null</c> for the usual icon for that name.</summary>
    public string? Icon { get; init; }
}

public sealed record UpdateCategoryRequest
{
    public string Name { get; init; } = string.Empty;

    public bool IsHidden { get; init; }

    /// <summary>A key from <see cref="CategoryIcons"/>; <c>null</c> keeps the current icon.</summary>
    public string? Icon { get; init; }
}

/// <summary>Shared by the form and the API.</summary>
public static class CategoryValidator
{
    public const int NameMaxLength = 50;

    /// <returns>An error keyed by "Icon" if the icon isn't one of <see cref="CategoryIcons.All"/>; empty when valid or not given.</returns>
    public static IReadOnlyDictionary<string, string[]> ValidateIcon(string? icon) =>
        icon is null || CategoryIcons.IsKnown(icon)
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]> { ["Icon"] = ["Choose one of the icons shown."] };

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
