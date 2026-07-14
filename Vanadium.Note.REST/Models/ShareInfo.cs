namespace Vanadium.Note.REST.Models;

/// <summary>
/// Share status of a note as returned to its owner. The owner is authenticated, so exposing the
/// token here is intentional — it is how the client builds the shareable link.
/// </summary>
public class ShareInfo
{
    public bool IsShared { get; set; }
    public ShareMode Mode { get; set; } = ShareMode.None;
    public string? Token { get; set; }
    public DateTime? SharedAt { get; set; }
}
