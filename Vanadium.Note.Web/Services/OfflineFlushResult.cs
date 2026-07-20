namespace Vanadium.Note.Web.Services;

/// <summary>
/// Outcome of one <see cref="OfflineSaveQueue.FlushAsync"/> pass (issue #211).
/// </summary>
/// <param name="Flushed">Parked saves that reached the server.</param>
/// <param name="Dropped">
/// Parked saves discarded because the note moved on without them. The caller surfaces
/// this to the user: dropping an entry destroys offline work, so it must never be a
/// silent, log-only event.
/// </param>
public readonly record struct OfflineFlushResult(int Flushed, int Dropped);
