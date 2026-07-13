namespace Vanadium.Note.Web.Models;

/// <summary>
/// Lean "what links here" backlink hit. Mirror of the REST DTO.
/// </summary>
public class BacklinkResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}
