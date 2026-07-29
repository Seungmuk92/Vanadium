using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

public record CreatePropertyDefinitionRequest(
    [Required][MaxLength(100)] string Name,
    PropertyType Type);

public record UpdatePropertyDefinitionRequest(
    [Required][MaxLength(100)] string Name,
    PropertyType Type,
    int SortOrder);

public record CreatePropertyOptionRequest([Required][MaxLength(100)] string Name);

public record UpdatePropertyOptionRequest(
    [Required][MaxLength(100)] string Name,
    int SortOrder);

/// <summary>Exactly the member(s) matching the definition's type must be set (§7.2).</summary>
public class SetNotePropertyValueRequest
{
    [MaxLength(500)]
    public string? TextValue { get; set; }      // Text
    public double? NumberValue { get; set; }    // Number
    public DateOnly? DateValue { get; set; }    // Date
    public bool? BoolValue { get; set; }        // Checkbox
    public Guid? OptionId { get; set; }         // Select
    public List<Guid>? OptionIds { get; set; }  // MultiSelect
}
