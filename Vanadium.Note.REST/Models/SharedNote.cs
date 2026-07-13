namespace Vanadium.Note.REST.Models;

/// <summary>
/// Read-only public projection of a shared note, served anonymously by <c>ShareController</c>.
/// Deliberately lean: it exposes only what a reader needs (title + sanitized HTML) and never the
/// share token, parent/child structure, labels, or lifecycle fields.
/// </summary>
public class SharedNote
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
