namespace Budget.Shared.Account;

/// <summary>
/// The current privacy notice and terms of use (BUD-152). Changing <see cref="TermsVersion"/> asks everyone to accept
/// the terms again when they next open the app. The wording is in <c>Pages/Legal</c> in the client.
/// </summary>
public static class LegalDocuments
{
    public const string TermsVersion = "0.1";

    public const string PrivacyVersion = "0.1";

    /// <summary>The wording hasn't been reviewed yet, so the pages say they're a draft.</summary>
    public static readonly bool IsDraft = true;

    public const string PrivacyPath = "/privacy";

    public const string TermsPath = "/terms";

    /// <summary>Where someone who hasn't accepted the current terms is sent.</summary>
    public const string AcceptPath = "/terms/accept";
}

/// <summary>Accept the terms of use (BUD-152). <see cref="Version"/> must be the current one.</summary>
public sealed record AcceptTermsRequest
{
    public string? Version { get; init; }
}
