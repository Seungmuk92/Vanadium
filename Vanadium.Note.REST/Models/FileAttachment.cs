using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

public class FileAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(255)]
    public string OriginalName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
