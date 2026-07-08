using System.Text.Json;
using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Persists the in-progress editor draft to the browser's <c>sessionStorage</c>
/// so that a mid-session auth expiry (401 → redirect to login) does not discard
/// unsaved work. Only a single draft slot is kept; it is keyed by note id so a
/// stashed draft is restored only when the same note is reopened after re-login
/// (issue #117). Mirrors <see cref="Vanadium.Note.Web.Auth.TokenStore"/>.
/// </summary>
public class DraftStore(IJSRuntime js, ILogger<DraftStore> logger)
{
    private const string StorageKey = "vanadium.editor-draft.v1";

    public async Task SaveAsync(Guid? noteId, string title, string content)
    {
        try
        {
            var json = JsonSerializer.Serialize(new EditorDraft(noteId, title, content));
            await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist editor draft to sessionStorage.");
        }
    }

    /// <summary>
    /// Returns the stashed draft only when it belongs to <paramref name="noteId"/>
    /// (both <c>null</c> matches a new-note draft); otherwise <c>null</c>.
    /// </summary>
    public async Task<EditorDraft?> GetAsync(Guid? noteId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
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

    public async Task ClearAsync()
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear editor draft from sessionStorage.");
        }
    }
}
