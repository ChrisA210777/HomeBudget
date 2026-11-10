using System.Text.Json.Nodes;

namespace Budget.Server.Features.DataExport;

/// <summary>
/// Version 2 to 3 (BUD-90): version 3 adds the account each income is paid into. A version 2 file has none, which
/// reads as "not recorded", so nothing needs changing.
/// </summary>
public sealed class ExportUpgraderV2 : IExportUpgrader
{
    public int FromVersion => 2;

    public void Upgrade(JsonObject document) => ArgumentNullException.ThrowIfNull(document);
}
