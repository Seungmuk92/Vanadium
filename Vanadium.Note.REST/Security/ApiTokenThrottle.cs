using Microsoft.Extensions.Options;

namespace Vanadium.Note.REST.Security;

/// <summary>
/// In-memory, process-global implementation of <see cref="IApiTokenThrottle"/>. A single
/// shared counter of consecutive failures drives an exponential backoff: the first
/// <see cref="ApiTokenThrottleOptions.FailureThreshold"/> failures are free, after which each
/// failure locks PAT authentication for <c>BaseDelaySeconds * 2^n</c> (capped at
/// <c>MaxDelaySeconds</c>). A successful authentication resets everything.
/// </summary>
/// <remarks>
/// Registered as a singleton so the counter is shared across all requests. State is held in
/// memory only, so it resets on restart — acceptable for a single-owner app whose goal is to
/// blunt token guessing and cap the resulting DB lookups, not to provide durable lock records.
/// The lock is a cheap short critical section; the <c>ApiTokens</c> query happens outside it.
/// This deliberately mirrors <see cref="LoginThrottle"/> (issue #218 / #198).
/// </remarks>
public sealed class ApiTokenThrottle : IApiTokenThrottle
{
    private readonly ApiTokenThrottleOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApiTokenThrottle> _logger;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public ApiTokenThrottle(
        IOptions<ApiTokenThrottleOptions> options,
        TimeProvider timeProvider,
        ILogger<ApiTokenThrottle> logger)
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
            // would let an attacker who keeps hammering a locked path extend the window
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
                "Global PAT authentication lockout engaged after {Failures} consecutive failures; locked for {Seconds}s.",
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
