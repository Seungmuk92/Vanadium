namespace Vanadium.Note.REST.Security;

/// <summary>
/// Configuration for <see cref="ApiTokenThrottle"/>, the global (IP-independent) PAT
/// authentication failure backoff. Bound from the <c>Auth:PatThrottle</c> configuration section.
/// </summary>
/// <remarks>
/// The PAT authentication path performs one <c>ApiTokens</c> lookup per request and, unlike
/// <c>/api/auth/login</c>, is not covered by the per-IP fixed-window rate limiter (the limiter
/// is applied per endpoint, whereas the handler runs during authentication across all endpoints).
/// This throttle caps the total rate of failing attempts so a flood of invalid tokens — from
/// distributed sources or forged <c>X-Forwarded-For</c> headers — cannot keep hammering the DB.
/// </remarks>
public sealed class ApiTokenThrottleOptions
{
    public const string SectionName = "Auth:PatThrottle";

    /// <summary>
    /// Number of consecutive global failures tolerated before any lockout applies.
    /// Keeps an occasional misconfigured client from immediately locking out valid tokens.
    /// </summary>
    public int FailureThreshold { get; set; } = 10;

    /// <summary>
    /// Lockout duration applied at the threshold. Each additional failure beyond the
    /// threshold doubles this (exponential backoff), capped at <see cref="MaxDelaySeconds"/>.
    /// </summary>
    public double BaseDelaySeconds { get; set; } = 30;

    /// <summary>Upper bound on a single lockout window, so the backoff never grows without limit.</summary>
    public int MaxDelaySeconds { get; set; } = 900;
}
