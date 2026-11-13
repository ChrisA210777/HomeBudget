using Budget.Infrastructure.Encryption;

namespace Budget.Infrastructure.Tests.Encryption;

/// <summary>Encrypting names and free text (BUD-166). The key is configured for the assembly (EncryptionSetup).</summary>
public class FieldEncryptionTests
{
    [Theory]
    [InlineData("Jane")]
    [InlineData("")]
    [InlineData("Ünïcödé 💷 names – and notes\nover two lines")]
    public void Values_come_back_as_they_went_in(string value)
    {
        var stored = FieldEncryption.Encrypt(value);

        Assert.StartsWith(FieldEncryption.Prefix, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(value.Length > 0 ? value : "\u0000", stored, StringComparison.Ordinal);
        Assert.Equal(value, FieldEncryption.Decrypt(stored));
    }

    [Fact]
    public void The_same_value_is_stored_differently_each_time()
    {
        Assert.NotEqual(FieldEncryption.Encrypt("Netflix"), FieldEncryption.Encrypt("Netflix"));
    }

    [Fact]
    public void Values_stored_before_encryption_are_read_as_they_are()
    {
        Assert.Equal("Council tax", FieldEncryption.Decrypt("Council tax"));
        Assert.False(FieldEncryption.IsEncrypted("Council tax"));
    }

    [Fact]
    public void A_changed_value_is_refused_rather_than_read_wrongly()
    {
        var stored = FieldEncryption.Encrypt("Mortgage");
        var prefix = stored[..(stored.LastIndexOf(':') + 1)];
        var payload = Convert.FromBase64String(stored[prefix.Length..]);
        payload[14] ^= 1;
        var tampered = prefix + Convert.ToBase64String(payload);

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => FieldEncryption.Decrypt(tampered));
    }

    [Fact]
    public void A_value_from_a_key_that_isnt_configured_says_which_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() => FieldEncryption.Decrypt("enc1:deadbeef:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        Assert.Contains("deadbeef", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuring_a_different_key_later_is_refused()
    {
        var other = Convert.ToBase64String(new byte[32]);

        Assert.Throws<InvalidOperationException>(() => FieldEncryption.Configure(other));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("@Microsoft.KeyVault(VaultName=kv;SecretName=data-encryption-key)")]
    [InlineData("c2hvcnQ=")]
    public void A_bad_key_stops_the_app_starting(string key)
    {
        Assert.Throws<InvalidOperationException>(() => FieldEncryption.Configure(key));
    }

    [Fact]
    public void Name_keys_ignore_case_and_spaces_but_not_spelling()
    {
        Assert.Equal(FieldEncryption.NameKey("Jane"), FieldEncryption.NameKey("  jane "));
        Assert.NotEqual(FieldEncryption.NameKey("Jane"), FieldEncryption.NameKey("Janet"));
        Assert.Equal(FieldEncryption.NameKeyLength, FieldEncryption.NameKey("Jane").Length);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(600)]
    public void The_longest_value_allowed_fits_its_column(int maxChars)
    {
        // Three-byte characters are the worst case for UTF-8 within one UTF-16 character.
        var stored = FieldEncryption.Encrypt(new string('€', maxChars));

        Assert.True(stored.Length <= FieldEncryption.StoredLength(maxChars), $"{stored.Length} > {FieldEncryption.StoredLength(maxChars)}");
    }

    [Fact]
    public void Very_long_text_goes_in_an_unlimited_column()
    {
        Assert.Null(FieldEncryption.StoredLength(2000));
    }
}
