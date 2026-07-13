namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>ShareInfo</c> DTO — a note's current share status.</summary>
public class ShareInfo
{
    public bool IsShared { get; set; }
    public ShareMode Mode { get; set; } = ShareMode.None;
    public string? Token { get; set; }
    public DateTime? SharedAt { get; set; }
}
