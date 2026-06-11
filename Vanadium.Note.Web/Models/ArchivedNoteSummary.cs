namespace Vanadium.Note.Web.Models;

public class ArchivedNoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime ArchivedAt { get; set; }

    /// <summary>Descendants archived in the same operation (same archive group).</summary>
    public int ChildCount { get; set; }
}
