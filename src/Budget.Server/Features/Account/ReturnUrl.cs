using System.Diagnostics.CodeAnalysis;

namespace Budget.Server.Features.Account;

/// <summary>
/// Makes sure we only ever redirect back into this app after sign-in, never to another site
/// (an "open redirect").
/// </summary>
public static class ReturnUrl
{
    public const string Default = "/";

    public static string Sanitize(string? url) => IsLocal(url) ? url : Default;

    /// <summary>Same rules as ASP.NET Core's <c>IUrlHelper.IsLocalUrl</c> for paths.</summary>
    public static bool IsLocal([NotNullWhen(true)] string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length == 1)
        {
            return true;
        }

        // "//host" and "/\host" are treated by browsers as links to another site.
        return url[1] != '/' && url[1] != '\\' && !url.Any(char.IsControl);
    }
}
