using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Stateless helpers shared by the note services (issue #311): HTML reference rewriting
/// (page-link / mention title propagation and stripping), the indexed "references this note"
/// query probe, plain-text derivation for the search column, and write-time timestamp
/// normalization. Every member is pure and static — it holds no <c>DbContext</c> or other
/// per-request state — so the CRUD (<see cref="NoteService"/>), lifecycle
/// (<see cref="NoteLifecycleService"/>) and share (<see cref="NoteShareService"/>) services can
/// all share one copy without depending back on the class they were split out of.
/// </summary>
public static class NoteReferenceRewriter
{
    // Shared AngleSharp parser for note-content rewriting (issue #306). HtmlParser is stateless and
    // safe to reuse across threads; each call parses into its own document, so no shared mutable state
    // leaks between callers.
    private static readonly HtmlParser ContentParser = new();

    /// <summary>
    /// Parses <paramref name="content"/> as a body-context HTML fragment, applies <paramref name="mutate"/>
    /// to the parsed DOM, and returns the reserialized fragment — or the original string unchanged when
    /// <paramref name="mutate"/> reports it edited nothing. This replaces the former <c>Regex.Replace</c>
    /// content rewriting (issue #306): a spec-compliant parser sets attribute and text values through the
    /// DOM API, so it is immune to the <c>'$'</c> substitution-string bug (issue #299) and to nesting or
    /// attribute-order variations that silently broke the regexes. The user-visible title always lands in
    /// element text content (never an attribute value), preserving the serialization hard rule, and callers
    /// still derive <c>ContentText</c> via <c>StripHtml</c> on the result.
    /// </summary>
    public static string RewriteContent(string content, Func<IElement, bool> mutate)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var document = ContentParser.ParseDocument(string.Empty);
        var body = document.Body!;
        body.InnerHtml = content;
        return mutate(body) ? body.InnerHtml : content;
    }

    public static string UpdateMentionTitleInContent(string content, Guid noteId, string newTitle) =>
        RewriteContent(content, body =>
        {
            // Mentions are the only <a> elements carrying data-note-id; page-links are <div>.
            var mentions = body.QuerySelectorAll($"a[data-note-id=\"{noteId}\"]");
            if (mentions.Length == 0) return false;
            foreach (var mention in mentions)
            {
                mention.SetAttribute("data-title", newTitle);
                mention.TextContent = "@" + newTitle;
            }
            return true;
        });

    public static string StripMentionLinksFromContent(string content, Guid noteId) =>
        RewriteContent(content, body =>
        {
            var mentions = body.QuerySelectorAll($"a[data-note-id=\"{noteId}\"]");
            if (mentions.Length == 0) return false;
            foreach (var mention in mentions)
            {
                // Unwrap the link: keep its visible text ("@Title"), drop the anchor wrapper.
                var text = mention.Owner!.CreateTextNode(mention.TextContent);
                mention.Parent!.ReplaceChild(text, mention);
            }
            return true;
        });

    public static string UpdatePageLinkTitleInContent(string content, Guid noteId, string newTitle) =>
        RewriteContent(content, body =>
        {
            // Page-links are the only <div> elements carrying data-note-id.
            var pageLinks = body.QuerySelectorAll($"div[data-note-id=\"{noteId}\"]");
            if (pageLinks.Length == 0) return false;
            foreach (var pageLink in pageLinks)
            {
                pageLink.SetAttribute("data-title", newTitle);
                pageLink.TextContent = "📄 " + newTitle;
            }
            return true;
        });

    public static string RemovePageLinkFromContent(string content, Guid noteId) =>
        RewriteContent(content, body =>
        {
            var pageLinks = body.QuerySelectorAll($"div[data-note-id=\"{noteId}\"]");
            if (pageLinks.Length == 0) return false;
            foreach (var pageLink in pageLinks)
                pageLink.Remove();
            return true;
        });

    /// <summary>
    /// Filters <paramref name="notes"/> to those whose HTML <c>Content</c> references note
    /// <paramref name="id"/> via a <c>data-note-id="{id}"</c> attribute — the shared scan behind
    /// backlinks, page-link title propagation and mention cleanup.
    /// <para>
    /// On PostgreSQL (<paramref name="isNpgsql"/> true) the probe is a case-sensitive
    /// <c>LIKE '%…%'</c> substring match, which the <c>gin_trgm_ops</c> index on <c>Content</c>
    /// (issue #219) accelerates instead of a full corpus scan (issue #220) — the reference lives in
    /// an HTML attribute value that <c>StripHtml</c> discards, so it never reaches <c>ContentText</c>
    /// and that column's trigram index cannot serve the scan. The needle is a fixed attribute name
    /// plus a GUID and never contains a LIKE wildcard, so the pattern needs no escaping. Other
    /// providers (the SQLite test host, which does not translate <c>LIKE</c> to an indexed probe)
    /// fall back to the original case-sensitive <c>Contains</c>, preserving identical match semantics.
    /// </para>
    /// </summary>
    public static IQueryable<NoteItem> WhereReferencesNote(IQueryable<NoteItem> notes, Guid id, bool isNpgsql)
    {
        var needle = $"data-note-id=\"{id}\"";
        if (isNpgsql)
        {
            var pattern = $"%{needle}%";
            return notes.Where(n => EF.Functions.Like(n.Content, pattern));
        }
        return notes.Where(n => n.Content.Contains(needle));
    }

    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex HtmlEntityRegex = new("&[a-zA-Z]+;|&#[0-9]+;", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Derives the plain-text search projection (<c>ContentText</c>) from note HTML by replacing
    /// tags and entities with spaces and collapsing whitespace — the tag → space rule the trigram
    /// search column depends on.
    /// </summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = HtmlTagRegex.Replace(html, " ");
        text = HtmlEntityRegex.Replace(text, " ");
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    /// <summary>
    /// The write-time timestamp for a note. PostgreSQL timestamps are stored at microsecond
    /// precision (6 digits), while .NET <see cref="DateTime"/> has 100-nanosecond precision
    /// (7 digits). Truncating before save ensures the value returned from the server matches what the
    /// DB stores, preventing false optimistic-concurrency conflicts.
    /// </summary>
    public static DateTime UtcNowMicroseconds()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks / 10 * 10, DateTimeKind.Utc);
    }
}
