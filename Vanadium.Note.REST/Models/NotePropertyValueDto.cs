namespace Vanadium.Note.REST.Models;

/// <summary>Read model for one non-empty value; embedded in NoteItem.Properties and
/// NoteSummary.Properties. Exactly the member(s) matching <see cref="Type"/> are set.</summary>
public class NotePropertyValueDto
{
    public Guid DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;   // denormalized for display
    public PropertyType Type { get; set; }
    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? OptionId { get; set; }
    public List<Guid> OptionIds { get; set; } = [];
}
