namespace Vanadium.Note.Web.Models;

/// <summary>
/// Lean search hit for the Quick Navigation palette. Mirror of the REST DTO.
/// </summary>
public class QuickNavResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}
