namespace Vanadium.Note.Web.Services;

/// <summary>
/// A note save that could not reach the server because the browser was offline, parked
/// in <c>localStorage</c> until connectivity returns (issue #211).
/// </summary>
/// <remarks>
/// The payload mirrors exactly the fields <c>NoteEditor</c> sends on a save, so a replay
/// is byte-for-byte the request that was missed — notably <see cref="ParentNoteId"/>,
/// which a note PUT would otherwise reset to root. <see cref="BaseUpdatedAt"/> is the
/// note version the edit was made against and is replayed as-is, so the server's
/// optimistic-concurrency check still rejects an edit that would clobber newer content.
/// Only existing notes are queued: a never-saved note has no id, and replaying it would
/// risk creating duplicates (that case is already covered by <see cref="DraftStore"/>).
/// </remarks>
public record PendingSave(
    Guid NoteId,
    string Title,
    string Content,
    Guid? ParentNoteId,
    DateTime BaseUpdatedAt);
