using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

/// <summary>One value of one property on one note. A missing row = empty value (INV-P1).
/// Exactly the column matching the definition's type is non-null; for MultiSelect the value
/// lives in <see cref="SelectedOptions"/> and all typed columns stay null.</summary>
public class NotePropertyValue
{
    public Guid NoteId { get; set; }
    public Guid DefinitionId { get; set; }

    [JsonIgnore]
    public NoteItem Note { get; set; } = null!;
    [JsonIgnore]
    public PropertyDefinition Definition { get; set; } = null!;

    [MaxLength(500)]
    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? SelectedOptionId { get; set; }

    [JsonIgnore]
    public PropertyOption? SelectedOption { get; set; }

    public ICollection<NotePropertySelectedOption> SelectedOptions { get; set; } = [];
}
