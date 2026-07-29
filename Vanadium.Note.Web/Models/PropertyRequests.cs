namespace Vanadium.Note.Web.Models;

public record CreatePropertyDefinitionRequest(string Name, PropertyType Type);

public record UpdatePropertyDefinitionRequest(string Name, PropertyType Type, int SortOrder);

public record CreatePropertyOptionRequest(string Name);

public record UpdatePropertyOptionRequest(string Name, int SortOrder);

/// <summary>Exactly the member matching the definition's type is set; the rest stay null.</summary>
public class SetNotePropertyValueRequest
{
    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? OptionId { get; set; }
    public List<Guid>? OptionIds { get; set; }
}
