using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Coordinates note saves that were made while the browser was offline (issue #211):
/// they are parked in <see cref="PendingSaveStore"/> and replayed once
/// <see cref="NetworkStatusService"/> reports the connection is back.
/// </summary>
/// <remarks>
/// <para>
/// An open editor <see cref="ClaimAsync">claims</see> the note it is editing, and the
/// flush skips claimed notes. The live editor holds fresher content than anything parked
/// for that note and re-saves it itself on reconnect, so without the claim BOTH would PUT
/// the same note; the loser of that race would take a 409 and show the user a conflict
/// banner for an edit nobody else touched. Claiming makes ownership of each note
/// unambiguous: exactly one writer per note, the editor when one is open and the queue
/// otherwise. Claims live in <c>localStorage</c> (see <see cref="NoteClaimStore"/>) so
/// that holds across tabs, not just within one.
/// </para>
/// <para>
/// A claim would strand the note's parked entry if the editor never saved it, so an
/// editor opening a note first <see cref="AdoptAsync">adopts</see> whatever is parked for
/// it. The entry stops depending on the flush and rides the editor's own save instead.
/// </para>
/// <para>
/// The queue is flushed from <c>MainLayout</c>, not from the editor, so a save parked
/// before the user navigated away — or before they closed the tab entirely — still lands.
/// </para>
/// </remarks>
public sealed class OfflineSaveQueue(
    PendingSaveStore store,
    NoteClaimStore claims,
    NoteService notes,
    ILogger<OfflineSaveQueue> logger) : IAsyncDisposable
{
    // Comfortably inside NoteClaimStore.Ttl so a claim never lapses under a live editor,
    // while still letting a killed tab's claim expire quickly.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly HashSet<Guid> _claimed = [];
    private CancellationTokenSource? _heartbeat;
    private bool _flushing;

    /// <summary>Marks <paramref name="noteId"/> as owned by a live editor in this tab.</summary>
    public async Task ClaimAsync(Guid noteId)
    {
        _claimed.Add(noteId);
        await claims.ClaimAsync(noteId);
        StartHeartbeat();
    }

    public async Task ReleaseAsync(Guid noteId)
    {
        _claimed.Remove(noteId);
        if (_claimed.Count == 0)
            StopHeartbeat();
        await claims.ReleaseAsync(noteId);
    }

    public Task EnqueueAsync(PendingSave entry) => store.EnqueueAsync(entry);

    /// <summary>
    /// Drops any parked save for <paramref name="noteId"/>. Called when a save for that
    /// note succeeds — the server now holds content at least as new as the parked copy.
    /// </summary>
    /// <remarks>
    /// Safe only because an editor <see cref="AdoptAsync">adopts</see> the parked entry
    /// when it opens a note: the content just saved therefore already contains the parked
    /// edit. Removing an entry the saving writer had never merged is exactly the silent
    /// data loss this pairing exists to prevent.
    /// </remarks>
    public Task DiscardAsync(Guid noteId) => store.RemoveAsync(noteId);

    /// <summary>
    /// Hands an opening editor the save parked for <paramref name="noteId"/> so it can
    /// merge it into its own state, or <c>null</c> when there is nothing to merge.
    /// </summary>
    /// <param name="serverUpdatedAt">The note version the editor just loaded.</param>
    /// <remarks>
    /// A parked entry whose base version is older than what the server now holds is stale:
    /// the note moved on without it, so it is dropped under the same rule the replay path
    /// applies to a 409. The adopted entry is deliberately LEFT in the store — if this tab
    /// closes before the editor manages to save, a later flush must still find it.
    /// </remarks>
    public async Task<PendingSave?> AdoptAsync(Guid noteId, DateTime serverUpdatedAt)
    {
        var entry = await store.GetAsync(noteId);
        if (entry is null)
            return null;

        if (entry.BaseUpdatedAt < serverUpdatedAt)
        {
            logger.LogWarning(
                "Discarding queued offline save for note {NoteId}: the server holds a newer version.",
                noteId);
            await store.RemoveAsync(noteId);
            return null;
        }

        return entry;
    }

    /// <summary>
    /// Replays every unclaimed parked save. Safe to call when there is nothing queued, and
    /// safe to call while still offline (the replays fail and the entries stay put for the
    /// next attempt).
    /// </summary>
    public async Task<OfflineFlushResult> FlushAsync()
    {
        // The start-up flush and an `online` event can land together — browsers also fire
        // spurious `online` events — and two concurrent passes would read the same list
        // and double-PUT every entry.
        if (_flushing)
            return default;

        _flushing = true;
        try
        {
            var pending = await store.GetAllAsync();
            if (pending.Count == 0)
                return default;

            var flushed = 0;
            var dropped = 0;
            foreach (var entry in pending)
            {
                if (_claimed.Contains(entry.NoteId) || await claims.IsClaimedAsync(entry.NoteId))
                    continue;

                var result = await notes.SaveAsync(new NoteItem
                {
                    Id = entry.NoteId,
                    Title = entry.Title,
                    Content = entry.Content,
                    ParentNoteId = entry.ParentNoteId,
                    UpdatedAt = entry.BaseUpdatedAt
                });

                if (result.IsSuccess)
                {
                    await store.RemoveAsync(entry.NoteId);
                    flushed++;
                }
                else if (result.IsConflict || result.IsForbidden || result.IsNotFound)
                {
                    // The note moved on without this edit: a newer version was saved
                    // elsewhere (409), it was archived and is read-only (403), or it is
                    // gone (404). Replaying can only clobber newer content or fail
                    // forever, so drop the entry instead of retrying it on every future
                    // reconnect — and report it so the loss is not silent.
                    logger.LogWarning(
                        "Dropping queued offline save for note {NoteId}: the server rejected the replay.",
                        entry.NoteId);
                    await store.RemoveAsync(entry.NoteId);
                    dropped++;
                }
                // Any other failure (still offline, 5xx) keeps the entry for the next flush.
            }

            return new OfflineFlushResult(flushed, dropped);
        }
        finally
        {
            _flushing = false;
        }
    }

    private void StartHeartbeat()
    {
        if (_heartbeat is not null)
            return;
        var cts = new CancellationTokenSource();
        _heartbeat = cts;
        _ = RefreshClaimsAsync(cts.Token);
    }

    private void StopHeartbeat()
    {
        _heartbeat?.Cancel();
        _heartbeat?.Dispose();
        _heartbeat = null;
    }

    private async Task RefreshClaimsAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, token);
                foreach (var noteId in _claimed.ToList())
                    await claims.ClaimAsync(noteId);
            }
        }
        catch (OperationCanceledException)
        {
            // The last claim was released, or the app is shutting down.
        }
        catch (Exception ex)
        {
            // A dead heartbeat only lets claims lapse, so log and stop rather than
            // letting an unobserved exception escape.
            logger.LogError(ex, "The offline save queue claim heartbeat stopped unexpectedly.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopHeartbeat();
        foreach (var noteId in _claimed.ToList())
            await claims.ReleaseAsync(noteId);
        _claimed.Clear();
    }
}
