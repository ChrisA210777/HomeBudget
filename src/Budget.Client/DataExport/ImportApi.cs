using System.Net.Http.Headers;
using System.Net.Http.Json;
using Budget.Shared.DataExport;

namespace Budget.Client.DataExport;

public interface IImportApi
{
    /// <summary>Uploads a file to be checked and summarised. Nothing is saved (BUD-67).</summary>
    Task<ImportPreviewResponse> PreviewAsync(Guid householdId, byte[] file);

    /// <summary>Imports the file as a new household owned by the signed-in user (BUD-63).</summary>
    /// <param name="me">The id, in the file, of the person who is the signed-in user, if any.</param>
    Task<ImportResultResponse> ImportAsNewHouseholdAsync(Guid householdId, byte[] file, string? name, Guid? me);

    /// <summary>Replaces this household's data with the file's (BUD-62). Members and their people stay.</summary>
    Task<ImportResultResponse> ReplaceAsync(Guid householdId, byte[] file, bool useFileName);
}

public sealed class ImportApi(HttpClient http) : IImportApi
{
    public Task<ImportPreviewResponse> PreviewAsync(Guid householdId, byte[] file) =>
        PostFileAsync<ImportPreviewResponse>(DataRoutes.Preview(householdId), file);

    public Task<ImportResultResponse> ImportAsNewHouseholdAsync(Guid householdId, byte[] file, string? name, Guid? me) =>
        PostFileAsync<ImportResultResponse>(DataRoutes.ImportAsNewHousehold(householdId, name, me), file);

    public Task<ImportResultResponse> ReplaceAsync(Guid householdId, byte[] file, bool useFileName) =>
        PostFileAsync<ImportResultResponse>(ReplaceRoutes.Replace(householdId, useFileName), file);

    private async Task<T> PostFileAsync<T>(string url, byte[] file)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync(new Uri(url, UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
