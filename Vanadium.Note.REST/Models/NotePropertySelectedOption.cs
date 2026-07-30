using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

/// <summary>One MultiSelect selection. PK (NoteId, DefinitionId, OptionId); the first two
/// columns tie it back to its <see cref="NotePropertyValue"/> parent row.</summary>
public class NotePropertySelectedOption
{
    public Guid NoteId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid OptionId { get; set; }

    [JsonIgnore]
    public NotePropertyValue Value { get; set; } = null!;
    [JsonIgnore]
    public PropertyOption Option { get; set; } = null!;
}
