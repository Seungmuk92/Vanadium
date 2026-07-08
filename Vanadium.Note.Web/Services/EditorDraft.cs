namespace Vanadium.Note.Web.Services;

/// <summary>
/// An in-progress editor draft (title + content) stashed in the browser's
/// <c>sessionStorage</c> when a save fails mid-session (e.g. an auth-expiry 401),
/// so the unsaved work can be restored when the same note is reopened after
/// re-login (issue #117). <see cref="NoteId"/> is <c>null</c> for a new,
/// not-yet-persisted note.
/// </summary>
public record EditorDraft(Guid? NoteId, string Title, string Content);
