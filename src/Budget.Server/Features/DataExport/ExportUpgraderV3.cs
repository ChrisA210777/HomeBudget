using System.Text.Json.Nodes;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Version 3 to 4 (BUD-91): version 4 adds transfers between accounts and the household's "Accounts and transfers"
/// setting. A version 3 file has neither, which reads as no transfers with the setting off, so nothing needs changing.
/// </summary>
public sealed class ExportUpgraderV3 : IExportUpgrader
{
    public int FromVersion => 3;

    public void Upgrade(JsonObject document) => ArgumentNullException.ThrowIfNull(document);
}
