using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vanadium.Note.REST.Security;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression tests for the global PAT authentication backoff (issue #218): consecutive failed
/// PAT authentications past a threshold must trigger an exponentially growing lockout that is
/// shared across all sources (so IP distribution / XFF forgery cannot bypass it), resets on a
/// successful authentication, and does not let attempts during a lockout extend it indefinitely.
/// The lockout is what caps the per-request <c>ApiTokens</c> DB lookups under a flood.
/// </summary>
public class ApiTokenThrottleTests
{
    private const int Threshold = 10;
    private const double BaseDelay = 30;
    private const int MaxDelay = 900;

    /// <summary>Minimal manual <see cref="TimeProvider"/> so tests control the clock without a new dependency.</summary>
    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static (ApiTokenThrottle Throttle, MutableTimeProvider Clock) CreateThrottle()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new ApiTokenThrottleOptions
        {
            FailureThreshold = Threshold,
            BaseDelaySeconds = BaseDelay,
            MaxDelaySeconds = MaxDelay,
        });
        var throttle = new ApiTokenThrottle(options, clock, NullLogger<ApiTokenThrottle>.Instance);
        return (throttle, clock);
    }

    [Fact]
    public void FailuresBelowThreshold_DoNotLock()
    {
        var (throttle, _) = CreateThrottle();

        for (var i = 0; i < Threshold - 1; i++)
            throttle.RegisterFailure();

        Assert.False(throttle.IsLocked(out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void FailuresAtThreshold_LockForBaseDelay()
    {
        var (throttle, _) = CreateThrottle();

        for (var i = 0; i < Threshold; i++)
            throttle.RegisterFailure();

        Assert.True(throttle.IsLocked(out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(BaseDelay), retryAfter);
    }

    [Fact]
    public void EachWindow_DoublesTheLockout_Exponentially()
    {
        var (throttle, clock) = CreateThrottle();

        for (var i = 0; i < Threshold; i++)
            throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out var first));
        Assert.Equal(TimeSpan.FromSeconds(30), first);

        clock.Advance(TimeSpan.FromSeconds(30));
        throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out var second));
        Assert.Equal(TimeSpan.FromSeconds(60), second);

        clock.Advance(TimeSpan.FromSeconds(60));
        throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out var third));
        Assert.Equal(TimeSpan.FromSeconds(120), third);
    }

    [Fact]
    public void Lockout_IsCappedAtMaxDelay()
    {
        var (throttle, clock) = CreateThrottle();

        // Drive far past the threshold, waiting out each window so every failure counts.
        for (var i = 0; i < Threshold + 20; i++)
        {
            throttle.RegisterFailure();
            throttle.IsLocked(out var retryAfter);
            clock.Advance(retryAfter);
        }

        throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out var capped));
        Assert.Equal(TimeSpan.FromSeconds(MaxDelay), capped);
    }

    [Fact]
    public void IsLocked_BecomesFalse_AfterWindowExpires()
    {
        var (throttle, clock) = CreateThrottle();

        for (var i = 0; i < Threshold; i++)
            throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out _));

        clock.Advance(TimeSpan.FromSeconds(BaseDelay));
        Assert.False(throttle.IsLocked(out _));
    }

    [Fact]
    public void RegisterSuccess_ClearsFailuresAndLockout()
    {
        var (throttle, _) = CreateThrottle();

        for (var i = 0; i < Threshold; i++)
            throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out _));

        throttle.RegisterSuccess();

        Assert.False(throttle.IsLocked(out _));
        // The counter reset too: a fresh run of sub-threshold failures must not re-lock.
        for (var i = 0; i < Threshold - 1; i++)
            throttle.RegisterFailure();
        Assert.False(throttle.IsLocked(out _));
    }

    [Fact]
    public void FailuresDuringActiveLockout_DoNotExtendIt()
    {
        var (throttle, clock) = CreateThrottle();

        for (var i = 0; i < Threshold; i++)
            throttle.RegisterFailure();
        Assert.True(throttle.IsLocked(out var initial));
        Assert.Equal(TimeSpan.FromSeconds(BaseDelay), initial);

        // Hammer the locked path. These must be ignored — the window must neither
        // grow nor its remaining time reset (lock-extension DoS protection).
        clock.Advance(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 50; i++)
            throttle.RegisterFailure();

        Assert.True(throttle.IsLocked(out var remaining));
        Assert.Equal(TimeSpan.FromSeconds(BaseDelay - 10), remaining);

        clock.Advance(TimeSpan.FromSeconds(BaseDelay - 10));
        Assert.False(throttle.IsLocked(out _));
    }

    [Fact]
    public void CounterIsSharedGlobally_RegardlessOfSource()
    {
        // The throttle holds no notion of client IP: every RegisterFailure feeds one counter,
        // which is exactly why spreading attempts across IPs / forged XFF cannot bypass it.
        var (throttle, _) = CreateThrottle();

        for (var i = 0; i < Threshold; i++)
            throttle.RegisterFailure();

        Assert.True(throttle.IsLocked(out _));
    }
}
