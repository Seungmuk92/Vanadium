namespace Vanadium.Note.Web.Models;

public class RecycleBinNoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; }
    public int ChildCount { get; set; }

    /// <summary>True when the note was archived at deletion time — restore
    /// returns it to the Archive, not the active list. Drives the UI badge.</summary>
    public bool IsArchived { get; set; }
}
