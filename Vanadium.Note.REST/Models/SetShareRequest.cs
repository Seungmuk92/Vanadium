namespace Vanadium.Note.REST.Models;

/// <summary>Body of <c>PUT /api/notes/{id}/share</c>: the desired share mode. Sending
/// <see cref="ShareMode.None"/> is equivalent to unsharing.</summary>
public class SetShareRequest
{
    public ShareMode Mode { get; set; } = ShareMode.None;
}
