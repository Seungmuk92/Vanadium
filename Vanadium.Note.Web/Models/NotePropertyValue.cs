namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>NotePropertyValueDto</c>: one non-empty property value on a note.</summary>
public class NotePropertyValue
{
    public Guid DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PropertyType Type { get; set; }
    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? OptionId { get; set; }
    public List<Guid> OptionIds { get; set; } = [];

    /// <summary>True when no member carries a value — the write-response shape after a clear.</summary>
    public bool IsEmpty =>
        TextValue is null && NumberValue is null && DateValue is null &&
        (BoolValue is null or false) && OptionId is null && OptionIds.Count == 0;
}
