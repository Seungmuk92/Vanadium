namespace Vanadium.Note.Web.Models;

public class NoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public Guid? ParentNoteId { get; set; }
    public int ChildCount { get; set; }
    public List<Label> Labels { get; set; } = [];
}
