namespace Vanadium.Note.Web.Models;

public class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optimistic-concurrency token echoed back to the server on save. It is a *semantic* field:
    /// its value must always be the server-issued timestamp the client last saw, never a
    /// client-invented one. The default is therefore <c>default</c> (an obviously-empty sentinel),
    /// NOT <c>DateTime.UtcNow</c> — a plausible-looking "now" default silently produced a version
    /// the server row could never match, so a construction that forgot to copy the tracked
    /// timestamp made every save 409 (issue #312). <c>default</c> fails the same way but reads as
    /// unmistakably unset; the server already rejects a default/zero version as a conflict (#221).
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Non-null = the note is archived; the editor switches to read-only mode.</summary>
    public DateTime? ArchivedAt { get; set; }

    public Guid? ParentNoteId { get; set; }
    public string? ParentTitle { get; set; }
    public int ChildCount { get; set; }

    /// <summary>Non-empty property values (issue #343). Server-owned — read-only here; edited via
    /// the dedicated property value endpoints, never posted back through note save.</summary>
    public List<NotePropertyValue> Properties { get; set; } = [];
}
