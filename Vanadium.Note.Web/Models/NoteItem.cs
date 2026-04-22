namespace Vanadium.Note.Web.Models;

public class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ParentNoteId { get; set; }
    public string? ParentTitle { get; set; }
    public int ChildCount { get; set; }
    public List<Label> Labels { get; set; } = [];
}
