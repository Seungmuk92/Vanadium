namespace Vanadium.Note.Web.Models;

/// <summary>
/// A recently visited note, stored client-side in localStorage (never sent to the server).
/// Used only to render the Quick Navigation empty-input state.
/// </summary>
public class RecentNote
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTime VisitedAt { get; set; }
}
