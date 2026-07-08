namespace Vanadium.Note.Web.Auth;

/// <summary>
/// Distinguishes the possible outcomes of a login attempt so the UI can show a
/// cause-specific message instead of collapsing every failure into "Invalid password".
/// </summary>
public enum LoginOutcome
{
    /// <summary>Login succeeded and a token was stored.</summary>
    Success,

    /// <summary>The server rejected the password (HTTP 401).</summary>
    InvalidPassword,

    /// <summary>The login endpoint is rate-limited (HTTP 429).</summary>
    RateLimited,

    /// <summary>The server returned an unexpected error (HTTP 5xx or a missing token).</summary>
    ServerError,

    /// <summary>The request never reached the server (offline, DNS, TLS, timeout).</summary>
    NetworkError,
}
