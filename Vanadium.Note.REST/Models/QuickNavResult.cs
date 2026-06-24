namespace Vanadium.Note.REST.Models;

/// <summary>
/// Lean search hit for the Quick Navigation palette.
/// Intentionally smaller than <see cref="NoteSummary"/>: no labels, child counts, or parent title.
/// </summary>
public class QuickNavResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Plain-text preview around the first match, tag-free (derived from ContentText). May be empty.</summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>True if the note is archived — drives the "Archived" badge. Such notes open read-only.</summary>
    public bool IsArchived { get; set; }
}
