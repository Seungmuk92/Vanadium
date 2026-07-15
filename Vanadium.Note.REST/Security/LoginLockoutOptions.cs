namespace Vanadium.Note.REST.Security;

/// <summary>
/// Configuration for <see cref="LoginThrottle"/>, the global (IP-independent) login
/// failure backoff. Bound from the <c>Auth:Lockout</c> configuration section.
/// </summary>
/// <remarks>
/// This complements the per-IP fixed-window rate limiter on <c>/api/auth/login</c>
/// (see <c>Program.cs</c>): the per-IP limiter caps one source, this global throttle
/// caps the total attempt rate so distributed sources or forged <c>X-Forwarded-For</c>
/// headers (issue #197) cannot dodge the cap by spreading attempts across IPs.
/// </remarks>
public sealed class LoginLockoutOptions
{
    public const string SectionName = "Auth:Lockout";

    /// <summary>
    /// Number of consecutive global failures tolerated before any lockout applies.
    /// Keeps an occasional typo by the legitimate owner from triggering a delay.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Lockout duration applied at the threshold. Each additional failure beyond the
    /// threshold doubles this (exponential backoff), capped at <see cref="MaxDelaySeconds"/>.
    /// </summary>
    public double BaseDelaySeconds { get; set; } = 30;

    /// <summary>Upper bound on a single lockout window, so the backoff never grows without limit.</summary>
    public int MaxDelaySeconds { get; set; } = 900;
}
