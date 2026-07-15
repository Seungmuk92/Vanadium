namespace Vanadium.Note.REST.Security;

/// <summary>
/// A process-global, IP-independent throttle on login attempts. Tracks consecutive
/// failed logins across every source and, past a threshold, imposes an exponentially
/// growing lockout window. Because the counter is global (not partitioned by client IP),
/// it cannot be bypassed by spreading attempts across many IPs or forging
/// <c>X-Forwarded-For</c> headers — unlike the per-IP fixed-window rate limiter.
/// </summary>
public interface ILoginThrottle
{
    /// <summary>
    /// Returns <c>true</c> if login is currently locked out. When locked,
    /// <paramref name="retryAfter"/> is the remaining time until attempts are accepted again.
    /// </summary>
    bool IsLocked(out TimeSpan retryAfter);

    /// <summary>Records a failed login attempt, extending the lockout once the threshold is crossed.</summary>
    void RegisterFailure();

    /// <summary>Records a successful login, clearing the failure count and any active lockout.</summary>
    void RegisterSuccess();
}
