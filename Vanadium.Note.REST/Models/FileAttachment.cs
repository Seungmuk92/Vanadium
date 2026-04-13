namespace Vanadium.Note.REST.Models;

public class FileAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
