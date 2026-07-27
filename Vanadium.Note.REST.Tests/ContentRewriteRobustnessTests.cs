using System.Net;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression coverage for issue #306: note-content rewriting (mention title propagation, mention
/// stripping, page-link title update) moved from <c>Regex.Replace</c> to AngleSharp DOM manipulation.
/// A spec-compliant parser must rewrite references correctly regardless of attribute order or how
/// deeply the reference is nested — the cases that made the regex approach fragile — while leaving the
/// surrounding structure untouched. The <c>'$'</c> substitution-string class (issue #299) is combined
/// in here too, since a DOM API cannot reintroduce it.
/// </summary>
public class ContentRewriteRobustnessTests
{
    /// <summary>Inserts an active note with exact content, bypassing the service sanitizer so the
    /// reordered / nested reference markup under test reaches the rewrite path verbatim.</summary>
    private static async Task<NoteItem> AddRawNoteAsync(TestHost h, string content)
    {
        var note = new NoteItem
        {
            Title = "Referencing",
            Content = content,
            ContentText = content,
        };
        h.Db.Notes.Add(note);
        await h.Db.SaveChangesAsync();
        return note;
    }

    [Fact]
    public async Task TitlePropagation_RewritesReorderedAndNestedReferences_PreservesSurroundings()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Old Title");

        // Attributes deliberately out of canonical order (data-title before data-note-id, data-type
        // last) and the references buried inside blockquote > ul > li, next to sibling content that
        // must survive the rewrite untouched.
        var referrer = await AddRawNoteAsync(h,
            "<blockquote><ul><li>See " +
            $"<div class=\"page-link-block\" data-title=\"Old Title\" data-note-id=\"{target.Id}\" data-type=\"page-link\">📄 Old Title</div>" +
            " and " +
            $"<a class=\"note-mention\" data-title=\"Old Title\" data-note-id=\"{target.Id}\" data-type=\"note-mention\">@Old Title</a>" +
            " here.</li></ul></blockquote>" +
            "<p>Sibling paragraph that must survive.</p>");

        // Rename to a title carrying substitution metacharacters ($&) that would have corrupted the
        // regex replacement string.
        const string newTitle = "New $& Title";
        var (_, conflict, _) = await h.Notes.Update(target.Id,
            new NoteItem { Title = newTitle, Content = target.Content, UpdatedAt = target.UpdatedAt });
        Assert.False(conflict);

        var fresh = await h.FindAsync(referrer.Id);
        var encoded = WebUtility.HtmlEncode(newTitle); // "New $&amp; Title"

        // Both references carry the new title verbatim, in the data-title attribute and visible text.
        Assert.Contains($"data-title=\"{encoded}\"", fresh!.Content);
        Assert.Contains($"📄 {encoded}</div>", fresh.Content);
        Assert.Contains($"@{encoded}</a>", fresh.Content);
        // The old title is gone everywhere, and the substitution metacharacters left no artifact.
        Assert.DoesNotContain("Old Title", fresh.Content);
        // Surrounding structure and sibling content are preserved.
        Assert.Contains("<blockquote>", fresh.Content);
        Assert.Contains("Sibling paragraph that must survive.", fresh.Content);
    }

    [Fact]
    public async Task MentionStrip_UnwrapsNestedMention_KeepsTextAndSiblings()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");

        var referrer = await AddRawNoteAsync(h,
            "<ul><li>Prefix " +
            $"<a class=\"note-mention\" data-title=\"Target\" data-note-id=\"{target.Id}\" data-type=\"note-mention\">@Target</a>" +
            " suffix</li></ul>");

        // Soft-delete then permanently delete the target — this strips dead mention links.
        Assert.True(await h.Notes.Delete(target.Id));
        var (found, wasInBin) = await h.Notes.DeletePermanent(target.Id);
        Assert.True(found);
        Assert.True(wasInBin);

        var fresh = await h.FindAsync(referrer.Id);
        // The link wrapper is gone but its visible text and the surrounding text remain intact.
        Assert.DoesNotContain($"data-note-id=\"{target.Id}\"", fresh!.Content);
        Assert.Contains("Prefix @Target suffix", fresh.Content);
    }
}
