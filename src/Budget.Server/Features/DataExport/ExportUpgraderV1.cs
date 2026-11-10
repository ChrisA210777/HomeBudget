using System.Text.Json.Nodes;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Version 1 to 2 (BUD-89): version 2 adds a list of accounts. A version 1 file has none; its outgoings' free-text
/// "paid from" names become accounts and pots when it's imported, as existing households' were (ADR-014).
/// </summary>
public sealed class ExportUpgraderV1 : IExportUpgrader
{
    public int FromVersion => 1;

    public void Upgrade(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document["accounts"] ??= new JsonArray();
    }
}
