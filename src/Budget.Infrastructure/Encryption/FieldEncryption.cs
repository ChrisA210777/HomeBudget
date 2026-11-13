using System.Security.Cryptography;
using System.Text;

namespace Budget.Infrastructure.Encryption;

/// <summary>
/// Encrypts names and free text before they're stored, so the database and its backups don't show who is who
/// (BUD-166, ADR-017). AES-256-GCM with a random nonce per value, so the same name encrypts differently each time.
/// Stored as <c>enc1:{key id}:{base64(nonce | ciphertext | tag)}</c>. Values without the prefix were stored before
/// encryption and are read as they are until <c>EncryptExistingData</c> encrypts them.
/// <para>
/// Configured once at start-up (<see cref="Configure"/>); EF Core's value converters call it. The key is a Key Vault
/// secret in Azure. Losing it loses the data, so a copy is kept offline (infra/README.md).
/// </para>
/// </summary>
public static class FieldEncryption
{
    public const string Prefix = "enc1:";

    /// <summary>The configuration section: <c>Encryption:Key</c> and <c>Encryption:PreviousKeys</c>.</summary>
    public const string ConfigSection = "Encryption";

    /// <summary>
    /// The key used in Development and the Testing environment when none is configured. Not a secret: it only ever
    /// protects local and test data. Deployed environments must set their own.
    /// </summary>
    public const string DevelopmentKey = "aG9tZS1idWRnZXQtZGV2ZWxvcG1lbnQta2V5LTAwMDE=";

    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyIdLength = 8;

    private static readonly object Gate = new();
    private static Keys? _keys;

    /// <summary>Whether <see cref="Configure"/> has been called.</summary>
    public static bool IsConfigured => _keys is not null;

    /// <summary>
    /// Sets the key used to encrypt, and any earlier keys still needed to read older values. Each is a base64 string of
    /// 32 random bytes. Calling it again with the same keys does nothing (several test hosts share one process).
    /// </summary>
    public static void Configure(string key, IEnumerable<string>? previousKeys = null)
    {
        var current = Parse(key, "Encryption:Key");
        var all = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [KeyId(current)] = current };
        foreach (var previous in previousKeys ?? [])
        {
            var bytes = Parse(previous, "Encryption:PreviousKeys");
            all.TryAdd(KeyId(bytes), bytes);
        }

        var keys = new Keys(KeyId(current), current, all, DeriveIndexKey(current));
        lock (Gate)
        {
            if (_keys is { } existing && existing.CurrentId != keys.CurrentId)
            {
                throw new InvalidOperationException("Data encryption is already configured with a different key.");
            }

            _keys = keys;
        }
    }

    /// <summary>Encrypts a value for storing.</summary>
    public static string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var keys = Current();
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceBytes + plain.Length + TagBytes];
        var nonce = payload.AsSpan(0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(keys.Current, TagBytes))
        {
            aes.Encrypt(nonce, plain, payload.AsSpan(NonceBytes, plain.Length), payload.AsSpan(NonceBytes + plain.Length, TagBytes));
        }

        return $"{Prefix}{keys.CurrentId}:{Convert.ToBase64String(payload)}";
    }

    /// <summary>Decrypts a stored value; one stored before encryption is returned as it is.</summary>
    public static string Decrypt(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!IsEncrypted(stored))
        {
            return stored;
        }

        var keys = Current();
        var keyId = stored.Substring(Prefix.Length, KeyIdLength);
        if (!keys.All.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException(
                $"A value was encrypted with a key that isn't configured (key id {keyId}). Add it to Encryption:PreviousKeys.");
        }

        var payload = Convert.FromBase64String(stored[(Prefix.Length + KeyIdLength + 1)..]);
        var length = payload.Length - NonceBytes - TagBytes;
        var plain = new byte[length];
        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Decrypt(payload.AsSpan(0, NonceBytes), payload.AsSpan(NonceBytes, length), payload.AsSpan(NonceBytes + length, TagBytes), plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Whether a stored value is encrypted (rather than stored before encryption).</summary>
    public static bool IsEncrypted(string stored) =>
        stored.StartsWith(Prefix, StringComparison.Ordinal) && stored.Length > Prefix.Length + KeyIdLength + 1
        && stored[Prefix.Length + KeyIdLength] == ':';

    /// <summary>
    /// A keyed hash of a name, the same for "Jane" and " jane ", so names can be checked for uniqueness without
    /// storing them readably. 44 characters.
    /// </summary>
    public static string NameKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalised = name.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        return Convert.ToBase64String(HMACSHA256.HashData(Current().IndexKey, Encoding.UTF8.GetBytes(normalised)));
    }

    /// <summary>The longest a name key is.</summary>
    public const int NameKeyLength = 44;

    /// <summary>
    /// The column length needed to store a value of up to <paramref name="maxChars"/> characters once encrypted, or
    /// <c>null</c> (no limit) when that's beyond what an indexed SQL Server column holds.
    /// </summary>
    public static int? StoredLength(int maxChars)
    {
        // Up to 3 UTF-8 bytes per UTF-16 character, plus nonce and tag, base64-encoded, plus the prefix and key id.
        var payload = NonceBytes + (3 * maxChars) + TagBytes;
        var length = Prefix.Length + KeyIdLength + 1 + (4 * ((payload + 2) / 3));
        return length <= 4000 ? length : null;
    }

    private static Keys Current() =>
        _keys ?? throw new InvalidOperationException("Data encryption isn't configured. Set Encryption:Key (see infra/README.md).");

    private static byte[] Parse(string value, string setting)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value?.Trim() ?? string.Empty);
        }
        catch (FormatException)
        {
            // In Azure an unresolved Key Vault reference arrives as its own text: say so rather than start without data.
            throw new InvalidOperationException($"{setting} isn't a base64 key. If it's a Key Vault reference, check it resolved.");
        }

        return bytes.Length == KeyBytes
            ? bytes
            : throw new InvalidOperationException($"{setting} must be {KeyBytes} bytes (base64), not {bytes.Length}.");
    }

    /// <summary>A short id for a key, stored with each value so the right key is used to read it.</summary>
    private static string KeyId(byte[] key) => Convert.ToHexStringLower(SHA256.HashData(key))[..KeyIdLength];

    private static byte[] DeriveIndexKey(byte[] key) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, KeyBytes, info: Encoding.UTF8.GetBytes("home-budget name index"));

    private sealed record Keys(string CurrentId, byte[] Current, IReadOnlyDictionary<string, byte[]> All, byte[] IndexKey);
}
