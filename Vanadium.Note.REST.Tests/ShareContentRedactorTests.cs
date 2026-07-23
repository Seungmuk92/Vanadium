using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Unit contract for <see cref="ShareContentRedactor"/> (issue #292): page-link and note-mention
/// nodes must be replaced with a reference-free placeholder so a shared note never leaks the
/// GUIDs/titles of the notes it links to, while ordinary formatting HTML is left untouched.
/// </summary>
public class ShareContentRedactorTests
{
    private const string SecretId = "11111111-2222-3333-4444-555555555555";
    private const string SecretTitle = "Top Secret Roadmap";

    private static string PageLink() =>
        $"<div data-type=\"page-link\" data-note-id=\"{SecretId}\" data-title=\"{SecretTitle}\" class=\"page-link-block\">" +
        $"<span class=\"page-link-icon\">📄</span><span class=\"page-link-title\">{SecretTitle}</span></div>";

    private static string Mention() =>
        $"<a data-type=\"note-mention\" data-note-id=\"{SecretId}\" data-title=\"{SecretTitle}\" class=\"note-mention\">@{SecretTitle}</a>";

    [Fact]
    public void Redact_PageLink_RemovesIdAndTitle()
    {
        var result = ShareContentRedactor.Redact($"<p>See </p>{PageLink()}");

        Assert.DoesNotContain(SecretId, result);
        Assert.DoesNotContain(SecretTitle, result);
        Assert.DoesNotContain("data-note-id", result);
        Assert.DoesNotContain("data-title", result);
        Assert.Contains("🔒 private page", result);
        Assert.Contains("<p>See </p>", result); // surrounding content preserved
    }

    [Fact]
    public void Redact_Mention_RemovesIdAndTitle()
    {
        var result = ShareContentRedactor.Redact($"<p>ping {Mention()} now</p>");

        Assert.DoesNotContain(SecretId, result);
        Assert.DoesNotContain(SecretTitle, result);
        Assert.DoesNotContain("data-note-id", result);
        Assert.DoesNotContain("data-title", result);
        Assert.Contains("🔒 private page", result);
        Assert.Contains("now</p>", result); // surrounding content preserved
    }

    [Fact]
    public void Redact_MultipleNodes_RedactsEach()
    {
        var result = ShareContentRedactor.Redact($"{PageLink()}<p>x</p>{Mention()}{PageLink()}");

        Assert.DoesNotContain(SecretId, result);
        Assert.DoesNotContain(SecretTitle, result);
        Assert.Equal(3, CountOccurrences(result, "🔒 private page"));
    }

    [Fact]
    public void Redact_AttributeOrderVariation_StillRedacts()
    {
        // class first, data-type not leading — mirrors possible attribute reordering.
        var reordered =
            $"<div class=\"page-link-block\" data-note-id=\"{SecretId}\" data-title=\"{SecretTitle}\" data-type=\"page-link\">" +
            $"<span>{SecretTitle}</span></div>";

        var result = ShareContentRedactor.Redact(reordered);

        Assert.DoesNotContain(SecretId, result);
        Assert.DoesNotContain(SecretTitle, result);
        Assert.Contains("🔒 private page", result);
    }

    [Fact]
    public void Redact_OrdinaryContent_Unchanged()
    {
        const string html = "<h1>Title</h1><p>Just <strong>text</strong> and a <a href=\"https://x.test\">link</a>.</p>";

        Assert.Equal(html, ShareContentRedactor.Redact(html));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Redact_NullOrEmpty_ReturnedUnchanged(string? html)
    {
        Assert.Equal(html, ShareContentRedactor.Redact(html!));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
