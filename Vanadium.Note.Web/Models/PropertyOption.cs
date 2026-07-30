namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>PropertyOptionDto</c>.</summary>
public class PropertyOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>Only populated when the list was fetched with includeUsage=true.</summary>
    public int? NoteCount { get; set; }
}
