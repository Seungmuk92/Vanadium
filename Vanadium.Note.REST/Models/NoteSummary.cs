namespace Vanadium.Note.REST.Models;

public class NoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public Guid? ParentNoteId { get; set; }
    public string? ParentTitle { get; set; }
    public int ChildCount { get; set; }

    /// <summary>True only in search results; drives the "Archived" badge in the UI.</summary>
    public bool IsArchived { get; set; }

    public List<LabelSummary> Labels { get; set; } = [];
}
