using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

/// <summary>One option of a Select/MultiSelect definition. The composite alternate
/// key (DefinitionId, Id) is what lets value rows reference an option of the same
/// definition at the DB level (INV-P3).</summary>
public class PropertyOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DefinitionId { get; set; }

    [JsonIgnore]
    public PropertyDefinition Definition { get; set; } = null!;

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Display order in pickers; also the sort key when sorting notes by a Select property.</summary>
    public int SortOrder { get; set; }
}
