using System.Text.Json;
using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Persists the in-progress editor draft to the browser's <c>sessionStorage</c>
/// so that a mid-session auth expiry (401 → redirect to login) does not discard
/// unsaved work. Each note gets its own slot, keyed by note id, so that saving
/// (or failing to save) one note never overwrites or clears another note's stashed
/// draft (issue #264). A stashed draft is restored only when the same note is
/// reopened after re-login (issue #117). Mirrors
/// <see cref="Vanadium.Note.Web.Auth.TokenStore"/>.
/// </summary>
public class DraftStore(IJSRuntime js, ILogger<DraftStore> logger)
{
    private const string StorageKeyPrefix = "vanadium.editor-draft.v1";

    // A per-note sessionStorage key. A new, not-yet-persisted note (null id) uses a
    // fixed "new" suffix so its draft is isolated from every saved note's slot.
    private static string StorageKey(Guid? noteId) =>
        $"{StorageKeyPrefix}.{(noteId.HasValue ? noteId.Value.ToString() : "new")}";

    public async Task SaveAsync(Guid? noteId, string title, string content)
    {
        try
        {
            var json = JsonSerializer.Serialize(new EditorDraft(noteId, title, content));
            await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey(noteId), json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist editor draft to sessionStorage.");
        }
    }

    /// <summary>
    /// Returns the stashed draft for <paramref name="noteId"/> (a <c>null</c> id
    /// reads the new-note slot); otherwise <c>null</c>. The stored note id is
    /// re-checked as a defensive guard against a mismatched/corrupt entry.
    /// </summary>
    public async Task<EditorDraft?> GetAsync(Guid? noteId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey(noteId));
            if (string.IsNullOrEmpty(json))
                return null;
            var draft = JsonSerializer.Deserialize<EditorDraft>(json);
            return draft is not null && draft.NoteId == noteId ? draft : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read editor draft from sessionStorage.");
            return null;
        }
    }

    /// <summary>
    /// Clears only <paramref name="noteId"/>'s draft slot (a <c>null</c> id clears the
    /// new-note slot), so a successful save of one note never discards another note's
    /// stashed draft (issue #264).
    /// </summary>
    public async Task ClearAsync(Guid? noteId)
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey(noteId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear editor draft from sessionStorage.");
        }
    }
}
