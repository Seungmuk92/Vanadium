namespace Vanadium.Note.REST.Models;

public class NoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public Guid? ParentNoteId { get; set; }
    public string? ParentTitle { get; set; }
    public int ChildCount { get; set; }
    public List<LabelSummary> Labels { get; set; } = [];
}
