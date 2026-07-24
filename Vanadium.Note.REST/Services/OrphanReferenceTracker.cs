using System.Collections.Concurrent;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Tracks, per uploaded-asset GUID, the UTC instant a periodic orphan scan first
/// observed the asset as unreferenced. The grace window before an orphan is deleted
/// is measured from this instant — NOT from the asset's upload/creation time — so a
/// freshly uploaded asset whose note draft has not yet been saved (auto-save delayed,
/// offline, or abandoned) is never collected on the first scan that sees it (issue #301).
/// An asset must be confirmed unreferenced across the whole grace window before GC; any
/// scan that finds it referenced again clears the record and resets the clock.
/// <para>
/// State is in-memory by design. A process restart clears the records, which only ever
/// DELAYS a delete (the first post-restart scan re-observes the asset and restarts its
/// grace clock) — it can never cause a premature delete, so the data-loss edge case the
/// grace window guards against stays fixed regardless of restart timing. The trade-off is
/// that a genuine orphan may linger a little longer; that is the intended safe direction.
/// </para>
/// </summary>
public sealed class OrphanReferenceTracker
{
    private readonly ConcurrentDictionary<Guid, DateTime> _firstUnreferencedUtc = new();

    /// <summary>
    /// Records <paramref name="nowUtc"/> as the first-unreferenced instant for
    /// <paramref name="id"/> if none is stored yet, and returns the stored instant.
    /// On the first observation this returns <paramref name="nowUtc"/> itself, so the
    /// caller sees the asset as still within its grace window.
    /// </summary>
    public DateTime ObserveUnreferenced(Guid id, DateTime nowUtc) =>
        _firstUnreferencedUtc.GetOrAdd(id, nowUtc);

    /// <summary>
    /// Forgets any first-unreferenced record for <paramref name="id"/>. Called when the
    /// asset is found referenced again (clock reset) or after it has been deleted.
    /// </summary>
    public void Forget(Guid id) => _firstUnreferencedUtc.TryRemove(id, out _);

    /// <summary>
    /// Drops records for every tracked GUID that is not in <paramref name="liveIds"/>,
    /// so assets that vanished by another path (manual delete, on-delete cleanup) do not
    /// leak entries. Called once at the end of a full scan.
    /// </summary>
    public void RetainOnly(IReadOnlySet<Guid> liveIds)
    {
        foreach (var tracked in _firstUnreferencedUtc.Keys)
        {
            if (!liveIds.Contains(tracked))
                _firstUnreferencedUtc.TryRemove(tracked, out _);
        }
    }
}
