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
/// <item>One entry PER NOTE, keyed by id. A note save is a full-document PUT, so the
/// newest edit for a note wholly supersedes any earlier queued edit for that note; the
/// queue is latest-wins rather than an append-only log of every keystroke burst.</item>
/// </list>
/// Every operation is best-effort: web storage can be full or disabled, and a failure to
/// park a save must never bubble up into the editor's save path.
/// </summary>
public sealed class PendingSaveStore(IJSRuntime js, ILogger<PendingSaveStore> logger)
{
    private const string StorageKey = "vanadium.pending-saves.v1";

    public async Task<List<PendingSave>> GetAllAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json))
                return [];
            return JsonSerializer.Deserialize<List<PendingSave>>(json) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read pending offline saves from localStorage.");
            return [];
        }
    }

    /// <summary>
    /// Parks <paramref name="entry"/>, replacing any earlier entry for the same note.
    /// </summary>
    public async Task EnqueueAsync(PendingSave entry)
    {
        try
        {
            var pending = await GetAllAsync();
            pending.RemoveAll(p => p.NoteId == entry.NoteId);
            pending.Add(entry);
            await WriteAsync(pending);
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
            var pending = await GetAllAsync();
            if (pending.RemoveAll(p => p.NoteId == noteId) == 0)
                return;
            await WriteAsync(pending);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove queued offline save for note {NoteId}.", noteId);
        }
    }

    private async Task WriteAsync(List<PendingSave> pending)
    {
        if (pending.Count == 0)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            return;
        }
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(pending));
    }
}
