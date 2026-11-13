using System.Runtime.CompilerServices;
using Budget.Infrastructure.Encryption;

namespace Budget.Infrastructure.Tests.Support;

/// <summary>Configures field encryption (BUD-166) once for every test in this assembly, as the app does at start-up.</summary>
internal static class EncryptionSetup
{
#pragma warning disable CA2255 // Intended: the key must be set before any test touches the database.
    [ModuleInitializer]
    internal static void Configure() => FieldEncryption.Configure(FieldEncryption.DevelopmentKey);
#pragma warning restore CA2255
}
