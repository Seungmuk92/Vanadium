namespace Vanadium.Note.REST.Security;

/// <summary>
/// A process-global, IP-independent throttle on personal access token (PAT) authentication
/// attempts. Tracks consecutive failed PAT authentications across every source and, past a
/// threshold, imposes an exponentially growing lockout window during which further attempts
/// are rejected before any database lookup. This blunts a flood of guessed/invalid tokens
/// that would otherwise cause one <c>ApiTokens</c> query per request. Because the counter is
/// global (not partitioned by client IP), it cannot be bypassed by spreading attempts across
/// many IPs or forging <c>X-Forwarded-For</c> headers — mirroring <see cref="ILoginThrottle"/>.
/// </summary>
public interface IApiTokenThrottle
{
    /// <summary>
    /// Returns <c>true</c> if PAT authentication is currently locked out. When locked,
    /// <paramref name="retryAfter"/> is the remaining time until attempts are accepted again.
    /// </summary>
    bool IsLocked(out TimeSpan retryAfter);

    /// <summary>Records a failed PAT authentication, extending the lockout once the threshold is crossed.</summary>
    void RegisterFailure();

    /// <summary>Records a successful PAT authentication, clearing the failure count and any active lockout.</summary>
    void RegisterSuccess();
}
