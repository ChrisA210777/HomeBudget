using System.Text.Json.Nodes;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Version 4 to 5 (BUD-136): version 5 adds when each account's month starts. A version 4 file doesn't say, which reads
/// as the 1st, as before, so nothing needs changing.
/// </summary>
public sealed class ExportUpgraderV4 : IExportUpgrader
{
    public int FromVersion => 4;

    public void Upgrade(JsonObject document) => ArgumentNullException.ThrowIfNull(document);
}
