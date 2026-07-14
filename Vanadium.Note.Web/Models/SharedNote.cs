namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>SharedNote</c> DTO — the read-only public projection of a
/// shared note returned by the anonymous share endpoint.</summary>
public class SharedNote
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
