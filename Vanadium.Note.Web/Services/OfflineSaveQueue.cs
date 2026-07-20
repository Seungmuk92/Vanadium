using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Coordinates note saves that were made while the browser was offline (issue #211):
/// they are parked in <see cref="PendingSaveStore"/> and replayed once
/// <see cref="NetworkStatusService"/> reports the connection is back.
/// </summary>
/// <remarks>
/// <para>
/// An open editor <see cref="Claim">claims</see> the note it is editing, and the flush
/// skips claimed notes. The live editor holds fresher content than anything parked for
/// that note and re-saves it itself on reconnect, so without the claim BOTH would PUT the
/// same note; the loser of that race would take a 409 and show the user a conflict banner
/// for an edit nobody else touched. Claiming makes ownership of each note unambiguous:
/// exactly one writer per note, the editor when one is open and the queue otherwise.
/// </para>
/// <para>
/// The queue is flushed from <c>MainLayout</c>, not from the editor, so a save parked
/// before the user navigated away — or before they closed the tab entirely — still lands.
/// </para>
/// </remarks>
public sealed class OfflineSaveQueue(
    PendingSaveStore store,
    NoteService notes,
    ILogger<OfflineSaveQueue> logger)
{
    private readonly HashSet<Guid> _claimed = [];

    /// <summary>Marks <paramref name="noteId"/> as owned by a live editor.</summary>
    public void Claim(Guid noteId) => _claimed.Add(noteId);

    public void Release(Guid noteId) => _claimed.Remove(noteId);

    public Task EnqueueAsync(PendingSave entry) => store.EnqueueAsync(entry);

    /// <summary>
    /// Drops any parked save for <paramref name="noteId"/>. Called when a save for that
    /// note succeeds — the server now holds content at least as new as the parked copy.
    /// </summary>
    public Task DiscardAsync(Guid noteId) => store.RemoveAsync(noteId);

    /// <summary>
    /// Replays every unclaimed parked save. Returns how many landed on the server.
    /// Safe to call when there is nothing queued, and safe to call while still offline
    /// (the replays fail and the entries stay put for the next attempt).
    /// </summary>
    public async Task<int> FlushAsync()
    {
        var pending = await store.GetAllAsync();
        if (pending.Count == 0)
            return 0;

        var flushed = 0;
        foreach (var entry in pending)
        {
            if (_claimed.Contains(entry.NoteId))
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
                // elsewhere (409), it was archived and is read-only (403), or it is gone
                // (404). Replaying can only clobber newer content or fail forever, so
                // drop the entry instead of retrying it on every future reconnect.
                logger.LogWarning(
                    "Dropping queued offline save for note {NoteId}: the server rejected the replay.",
                    entry.NoteId);
                await store.RemoveAsync(entry.NoteId);
            }
            // Any other failure (still offline, 5xx) keeps the entry for the next flush.
        }

        return flushed;
    }
}
