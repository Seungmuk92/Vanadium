namespace Vanadium.Note.Web.Models;

public class RecycleBinNoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; }
    public int ChildCount { get; set; }
}
