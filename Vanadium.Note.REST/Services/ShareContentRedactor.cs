using System.Text.RegularExpressions;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Redacts cross-note reference markup from a note's HTML before it is served on the
/// anonymous share path (issue #292).
///
/// <para>
/// Page-link (<c>&lt;div data-type="page-link"&gt;</c>) and note-mention
/// (<c>&lt;a data-type="note-mention"&gt;</c>) nodes embed the referenced note's
/// <c>data-note-id</c> (internal GUID) and <c>data-title</c> (its title, which also appears
/// as the node's visible text). Sharing one note would otherwise leak the titles and GUIDs of
/// every private note it links to. Each such node is replaced with a static
/// <c>🔒 private page</c> placeholder that carries no id/title, so an anonymous viewer learns
/// nothing about the referenced note beyond that a reference existed.
/// </para>
///
/// <para>
/// This runs only on the share response assembly (<c>ShareController</c>); the stored content
/// and the owner-facing editor rendering are untouched. The regex targets are the same
/// <c>data-type</c>-anchored shapes the editor serializes (mirroring the id-scoped rewrites in
/// <c>NoteService</c>): both nodes are atoms whose only inner content is spans / text, so the
/// first matching close tag is always the node's own.
/// </para>
/// </summary>
public static class ShareContentRedactor
{
    private const string Placeholder = "🔒 private page";

    // Page-link renders as a block <div data-type="page-link" ...> with two inner <span>s and no
    // nested <div>, so a non-greedy match to the first </div> captures exactly the node.
    private static readonly Regex PageLinkRegex = new(
        @"<div\b[^>]*\bdata-type=""page-link""[^>]*>.*?</div>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Note-mention renders as an inline <a data-type="note-mention" ...>@title</a> with text-only
    // content and no nested <a>, so a non-greedy match to the first </a> captures exactly the node.
    private static readonly Regex MentionRegex = new(
        @"<a\b[^>]*\bdata-type=""note-mention""[^>]*>.*?</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns <paramref name="html"/> with every page-link and note-mention node replaced by a
    /// reference-free placeholder. Null/empty input is returned unchanged.
    /// </summary>
    public static string Redact(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        var result = PageLinkRegex.Replace(html, $"<div class=\"private-page-ref\">{Placeholder}</div>");
        result = MentionRegex.Replace(result, $"<span class=\"private-page-ref\">{Placeholder}</span>");
        return result;
    }
}
