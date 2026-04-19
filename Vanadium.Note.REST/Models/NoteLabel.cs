namespace Vanadium.Note.REST.Models;

public class NoteLabel
{
    public Guid NoteId { get; set; }
    public Guid LabelId { get; set; }
    public NoteItem Note { get; set; } = null!;
    public Label Label { get; set; } = null!;
}
