using Microsoft.Extensions.Options;

namespace Vanadium.Note.REST.Security;

/// <summary>
/// In-memory, process-global implementation of <see cref="ILoginThrottle"/>. A single
/// shared counter of consecutive failures drives an exponential backoff: the first
/// <see cref="LoginLockoutOptions.FailureThreshold"/> failures are free, after which each
/// failure locks logins for <c>BaseDelaySeconds * 2^n</c> (capped at <c>MaxDelaySeconds</c>).
/// A successful login resets everything.
/// </summary>
/// <remarks>
/// Registered as a singleton so the counter is shared across all requests. State is held
/// in memory only, so it resets on restart — acceptable for a single-owner app whose goal
/// is to blunt online guessing, not to provide durable lock records. The lock is a cheap
/// short critical section; the expensive PBKDF2 verification happens outside it.
/// </remarks>
public sealed class LoginThrottle : ILoginThrottle
{
    private readonly LoginLockoutOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LoginThrottle> _logger;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public LoginThrottle(
        IOptions<LoginLockoutOptions> options,
        TimeProvider timeProvider,
        ILogger<LoginThrottle> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsLocked(out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            if (now < _lockedUntil)
            {
                retryAfter = _lockedUntil - now;
                return true;
            }

            retryAfter = TimeSpan.Zero;
            return false;
        }
    }

    public void RegisterFailure()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();

            // Ignore failures that arrive during an active lockout window. Counting them
            // would let an attacker who keeps hammering a locked login extend the window
            // forever, locking the legitimate owner out indefinitely (a self-inflicted DoS).
            if (now < _lockedUntil)
                return;

            _consecutiveFailures++;
            if (_consecutiveFailures < _options.FailureThreshold)
                return;

            var exponent = _consecutiveFailures - _options.FailureThreshold;
            var seconds = Math.Min(
                _options.BaseDelaySeconds * Math.Pow(2, exponent),
                _options.MaxDelaySeconds);
            _lockedUntil = now.AddSeconds(seconds);

            _logger.LogWarning(
                "Global login lockout engaged after {Failures} consecutive failures; locked for {Seconds}s.",
                _consecutiveFailures, seconds);
        }
    }

    public void RegisterSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _lockedUntil = DateTimeOffset.MinValue;
        }
    }
}
