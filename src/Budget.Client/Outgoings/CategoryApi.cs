using System.Net;
using System.Net.Http.Json;
using Budget.Shared.Outgoings;

namespace Budget.Client.Outgoings;

/// <summary>The outcome of a category change: whether it worked, or the errors to show.</summary>
public sealed record CategoryResult(bool Succeeded, IReadOnlyDictionary<string, string[]> Errors, string? Problem = null)
{
    public static readonly CategoryResult Success = new(true, new Dictionary<string, string[]>());
}

public interface ICategoryApi
{
    Task<IReadOnlyList<OutgoingCategorySummary>> ListAsync(Guid householdId);

    Task<CategoryResult> AddAsync(Guid householdId, AddCategoryRequest request);

    Task<CategoryResult> UpdateAsync(Guid householdId, Guid categoryId, UpdateCategoryRequest request);

    Task<CategoryResult> DeleteAsync(Guid householdId, Guid categoryId);
}

public sealed class CategoryApi(HttpClient http) : ICategoryApi
{
    public async Task<IReadOnlyList<OutgoingCategorySummary>> ListAsync(Guid householdId) =>
        await http.GetFromJsonAsync<List<OutgoingCategorySummary>>(Relative(OutgoingCategoryRoutes.Categories(householdId))) ?? [];

    public async Task<CategoryResult> AddAsync(Guid householdId, AddCategoryRequest request)
    {
        using var response = await http.PostAsJsonAsync(Relative(OutgoingCategoryRoutes.Categories(householdId)), request);
        return await ToResultAsync(response);
    }

    public async Task<CategoryResult> UpdateAsync(Guid householdId, Guid categoryId, UpdateCategoryRequest request)
    {
        using var response = await http.PutAsJsonAsync(Relative(OutgoingCategoryRoutes.Category(householdId, categoryId)), request);
        return await ToResultAsync(response);
    }

    public async Task<CategoryResult> DeleteAsync(Guid householdId, Guid categoryId)
    {
        using var response = await http.DeleteAsync(Relative(OutgoingCategoryRoutes.Category(householdId, categoryId)));
        return await ToResultAsync(response);
    }

    private static async Task<CategoryResult> ToResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return CategoryResult.Success;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
            return new CategoryResult(false, problem?.Errors ?? [], problem?.Title);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
            return new CategoryResult(false, new Dictionary<string, string[]>(), $"{problem?.Title} {problem?.Detail}".Trim());
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable.");
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    private sealed class ProblemResponse
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }

        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
