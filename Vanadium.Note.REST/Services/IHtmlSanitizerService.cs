namespace Vanadium.Note.REST.Services;

/// <summary>
/// Sanitizes note HTML before it is persisted, stripping active content
/// (scripts, event handlers, dangerous URI schemes) while preserving the
/// Tiptap editor's markup — including every <c>data-type</c>/<c>data-*</c>
/// attribute that custom nodes and ContentText derivation depend on.
/// </summary>
public interface IHtmlSanitizerService
{
    /// <summary>
    /// Returns a sanitized copy of <paramref name="html"/>. Null/empty input
    /// is returned unchanged.
    /// </summary>
    string Sanitize(string html);
}
