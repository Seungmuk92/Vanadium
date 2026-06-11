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

    /// <summary>Null = active. Non-null marks the note as soft-deleted and is the purge clock.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>True only on the note the user deleted directly (the restore target).
    /// Sub-notes swept into the recycle bin with a parent keep this false.</summary>
    [JsonIgnore]
    public bool IsDeletionRoot { get; set; }

    public Guid UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

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
