using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

/// <summary>A globally-defined property: a name, a type, a display order, and (for
/// Select/MultiSelect) an ordered option list. Notes carry only values for these
/// definitions, never their own definitions.</summary>
public class PropertyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public PropertyType Type { get; set; }

    /// <summary>Display order in the editor panel, filter menu, and /properties page.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PropertyOption> Options { get; set; } = [];
}
