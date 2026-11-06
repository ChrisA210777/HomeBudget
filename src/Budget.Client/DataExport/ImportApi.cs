using System.Net.Http.Headers;
using System.Net.Http.Json;
using Budget.Shared.DataExport;

namespace Budget.Client.DataExport;

public interface IImportApi
{
    /// <summary>Uploads a file to be checked and summarised. Nothing is saved (BUD-67).</summary>
    Task<ImportPreviewResponse> PreviewAsync(Guid householdId, byte[] file);
}

public sealed class ImportApi(HttpClient http) : IImportApi
{
    public async Task<ImportPreviewResponse> PreviewAsync(Guid householdId, byte[] file)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.PostAsync(new Uri(DataRoutes.Preview(householdId), UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportPreviewResponse>())!;
    }
}
