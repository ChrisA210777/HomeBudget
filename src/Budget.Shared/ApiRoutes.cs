namespace Budget.Shared;

/// <summary>
/// Route constants shared by the server and the client so the two cannot drift apart.
/// </summary>
public static class ApiRoutes
{
    /// <summary>Is the app running? Doesn't touch the database, so checking it doesn't wake the free-tier database.</summary>
    public const string Health = "/health";

    /// <summary>Can the app reach the database? For checking a deployment (BUD-12).</summary>
    public const string Ready = "/health/ready";

    /// <summary>
    /// Response header with the request's correlation id: Application Insights' operation id. The client shows it when
    /// something goes wrong on the server, so a problem can be found in the logs (BUD-12).
    /// </summary>
    public const string CorrelationIdHeaderName = "X-Correlation-Id";

    public const string ApiBase = "/api";

    /// <summary>
    /// Header the client sends on every request. State-changing requests to <c>/api</c> and <c>/bff</c>
    /// are rejected without it. Browsers won't add custom headers to cross-site requests without
    /// a CORS preflight, which this app doesn't allow, so this blocks cross-site request forgery.
    /// </summary>
    public const string CsrfHeaderName = "X-CSRF";
}

/// <summary>Routes for signing in and finding out who is signed in (Backend-for-Frontend, see ADR-001).</summary>
public static class AccountRoutes
{
    /// <summary>GET: the external sign-in providers that are configured.</summary>
    public const string Providers = "/account/providers";

    /// <summary>GET: starts sign-in with a provider. Navigate the browser here (full page load).</summary>
    public const string LoginTemplate = "/account/login/{provider}";

    /// <summary>GET: where the server finishes sign-in after the provider redirects back.</summary>
    public const string ExternalCallback = "/account/external-callback";

    /// <summary>GET: the signed-in user, or 401 if nobody is signed in.</summary>
    public const string CurrentUser = "/bff/user";

    /// <summary>PUT: switch the signed-in user's active household (BUD-20). 404 if they aren't a member.</summary>
    public const string ActiveHousehold = "/bff/user/active-household";

    /// <summary>GET: the signed-in user's settings. PUT: change some of them (BUD-87).</summary>
    public const string Settings = "/bff/user/settings";

    /// <summary>GET: what deleting the signed-in user's account would do (BUD-151).</summary>
    public const string AccountDeletion = "/bff/user/deletion";

    /// <summary>POST <c>DeleteAccountRequest</c>: deletes the signed-in user's account and signs them out (BUD-151).</summary>
    public const string DeleteAccount = "/bff/user/delete";

    /// <summary>POST: signs out by clearing the session cookie. Returns 204.</summary>
    public const string Logout = "/bff/logout";

    /// <summary>The client page that shows the sign-in options.</summary>
    public const string LoginPage = "/login";

    public static string Login(string provider, string returnUrl) =>
        $"/account/login/{Uri.EscapeDataString(provider)}?returnUrl={Uri.EscapeDataString(returnUrl)}";

    public static string LoginPageWithError(string error) => $"{LoginPage}?error={Uri.EscapeDataString(error)}";

    /// <summary>The sign-in page, confirming the person has signed out.</summary>
    public const string LoginPageAfterSignOut = LoginPage + "?signedOut=true";

    /// <summary>The sign-in page, confirming the person's account has been deleted (BUD-151).</summary>
    public const string LoginPageAfterAccountDeleted = LoginPage + "?accountDeleted=true";
}

/// <summary>Values of the <c>error</c> query parameter on the sign-in page.</summary>
public static class LoginErrors
{
    public const string Cancelled = "cancelled";

    public const string Failed = "failed";
}
