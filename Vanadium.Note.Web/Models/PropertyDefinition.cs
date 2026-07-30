namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>PropertyDefinitionDto</c>.</summary>
public class PropertyDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PropertyType Type { get; set; }
    public int SortOrder { get; set; }
    public List<PropertyOption> Options { get; set; } = [];

    /// <summary>Only populated when the list was fetched with includeUsage=true.</summary>
    public int? ValueCount { get; set; }
}
