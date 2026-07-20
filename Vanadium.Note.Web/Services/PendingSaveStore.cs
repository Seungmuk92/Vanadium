using System.Text.Json;
using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// A <c>localStorage</c>-backed queue of note saves that failed while the browser was
/// offline (issue #211). Mirrors <see cref="DraftStore"/>'s best-effort
/// JSON-in-web-storage pattern with two deliberate differences:
/// <list type="bullet">
/// <item><c>localStorage</c>, not <c>sessionStorage</c> — a queued save must survive the
/// tab being closed while offline, which is precisely when work would otherwise be lost.</item>
/// <item>One entry PER NOTE, stored under its OWN key. A note save is a full-document PUT,
/// so the newest edit for a note wholly supersedes any earlier queued edit for that note;
/// the queue is latest-wins rather than an append-only log of every keystroke burst.</item>
/// </list>
/// <para>
/// The per-note key is what makes the queue safe. Holding every entry in one JSON array
/// forced each enqueue and removal into a read-modify-write spanning three interop
/// round-trips with no lock, so two interleaved writers — two tabs, or an enqueue racing
/// a flush — could write back a list that silently dropped the other's entry. With one key
/// per note, an enqueue is a single <c>setItem</c> and a removal a single <c>removeItem</c>:
/// there is no window to interleave, and writers for different notes never touch the same key.
/// </para>
/// Every operation is best-effort: web storage can be full or disabled, and a failure to
/// park a save must never bubble up into the editor's save path.
/// </summary>
public sealed class PendingSaveStore(IJSRuntime js, ILogger<PendingSaveStore> logger)
{
    private const string KeyPrefix = "vanadium.pending-save.v1.";

    private static string KeyFor(Guid noteId) => KeyPrefix + noteId.ToString("D");

    public async Task<List<PendingSave>> GetAllAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string[]>("offlineStore.valuesWithPrefix", KeyPrefix);
            var entries = new List<PendingSave>(raw.Length);
            foreach (var json in raw)
            {
                var entry = Deserialize(json);
                if (entry is not null)
                    entries.Add(entry);
            }
            return entries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read pending offline saves from localStorage.");
            return [];
        }
    }

    public async Task<PendingSave?> GetAsync(Guid noteId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", KeyFor(noteId));
            return string.IsNullOrEmpty(json) ? null : Deserialize(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read queued offline save for note {NoteId}.", noteId);
            return null;
        }
    }

    /// <summary>
    /// Parks <paramref name="entry"/>, replacing any earlier entry for the same note in a
    /// single atomic write.
    /// </summary>
    public async Task EnqueueAsync(PendingSave entry)
    {
        try
        {
            await js.InvokeVoidAsync(
                "localStorage.setItem", KeyFor(entry.NoteId), JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue offline save for note {NoteId}.", entry.NoteId);
        }
    }

    public async Task RemoveAsync(Guid noteId)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", KeyFor(noteId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove queued offline save for note {NoteId}.", noteId);
        }
    }

    private PendingSave? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingSave>(json);
        }
        catch (JsonException ex)
        {
            // A corrupt entry must not sink the whole queue — skip it and keep the rest.
            logger.LogError(ex, "Discarding an unreadable queued offline save.");
            return null;
        }
    }
}
