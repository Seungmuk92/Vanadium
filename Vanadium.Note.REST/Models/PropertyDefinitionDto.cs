namespace Vanadium.Note.REST.Models;

public class PropertyDefinitionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PropertyType Type { get; set; }
    public int SortOrder { get; set; }
    public List<PropertyOptionDto> Options { get; set; } = [];

    /// <summary>Only populated when includeUsage=true. Counted with IgnoreQueryFilters().</summary>
    public int? ValueCount { get; set; }
}
