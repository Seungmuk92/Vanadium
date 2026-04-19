using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

public class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public List<LabelSummary> Labels { get; set; } = [];

    [JsonIgnore]
    public ICollection<NoteLabel> NoteLabels { get; set; } = [];
}
