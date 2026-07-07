using Ganss.Xss;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// <see cref="IHtmlSanitizerService"/> backed by Ganss.Xss (HtmlSanitizer).
///
/// The default HtmlSanitizer allowlist already covers the standard formatting
/// tags Tiptap emits (headings, lists, tables, code, blockquote, img, a, div,
/// span, ...) and removes <c>&lt;script&gt;</c>, inline event handlers
/// (<c>onerror</c>, <c>onclick</c>, ...) and unsafe URI schemes
/// (<c>javascript:</c>). Two Tiptap-specific allowances are added on top:
///
/// <list type="bullet">
///   <item>Every <c>data-*</c> attribute is preserved — custom nodes serialize
///   as <c>div/span/a/pre</c> carrying <c>data-type</c> plus node attributes
///   (<c>data-note-id</c>, <c>data-emoji</c>, <c>data-open</c>, ...), and the
///   collapsible-heading feature relies on <c>data-collapsible</c>/<c>data-open</c>.
///   Dropping them would break round-tripping and search derivation.</item>
///   <item>The <c>download</c> attribute is allowed for file-attachment anchors
///   (<c>&lt;a class="file-attachment" download="..."&gt;</c>).</item>
/// </list>
///
/// A single HtmlSanitizer instance is reused; it is stateless per call and safe
/// to share, so this service is registered as a singleton.
/// </summary>
public sealed class HtmlSanitizerService : IHtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();

        // Keep all data-* attributes: they carry custom-node identity/state and
        // must survive sanitizing. Event handlers (on*) are still stripped
        // because this only cancels removal for the data- prefix.
        _sanitizer.RemovingAttribute += (_, e) =>
        {
            if (e.Attribute.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        };

        // File-attachment anchors render as <a download="filename">.
        _sanitizer.AllowedAttributes.Add("download");
    }

    public string Sanitize(string html) =>
        string.IsNullOrEmpty(html) ? html : _sanitizer.Sanitize(html);
}
