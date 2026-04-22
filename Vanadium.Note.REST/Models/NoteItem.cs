using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

public class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(2_000_000)]
    public string Content { get; set; } = string.Empty;
    [JsonIgnore]
    public string ContentText { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid? ParentNoteId { get; set; }

    [JsonIgnore]
    public NoteItem? ParentNote { get; set; }

    [JsonIgnore]
    public ICollection<NoteItem> ChildNotes { get; set; } = [];

    [NotMapped]
    public int ChildCount { get; set; }

    [NotMapped]
    public string? ParentTitle { get; set; }

    [NotMapped]
    public List<LabelSummary> Labels { get; set; } = [];

    [JsonIgnore]
    public ICollection<NoteLabel> NoteLabels { get; set; } = [];
}
