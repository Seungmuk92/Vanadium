namespace Vanadium.Note.REST.Models;

/// <summary>
/// Lean "what links here" backlink hit: a note whose HTML content references the
/// current note via a <c>data-note-id="{id}"</c> attribute (mention or page link).
/// Intentionally smaller than <see cref="NoteSummary"/>: no labels, child counts, or parent title.
/// </summary>
public class BacklinkResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Plain-text preview of the referencing note, tag-free (derived from ContentText). May be empty.</summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>True if the referencing note is archived — drives the "Archived" badge.</summary>
    public bool IsArchived { get; set; }
}
