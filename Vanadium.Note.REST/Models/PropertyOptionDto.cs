namespace Vanadium.Note.REST.Models;

public class PropertyOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>Only populated when includeUsage=true. Counted with IgnoreQueryFilters().</summary>
    public int? NoteCount { get; set; }
}
