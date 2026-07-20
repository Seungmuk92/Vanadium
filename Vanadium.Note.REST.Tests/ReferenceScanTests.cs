using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression tests for issue #220: backlinks, page-link title propagation and
/// mention cleanup all share the reference probe (<c>NoteService.WhereReferencesNote</c>),
/// which on PostgreSQL became an index-friendly <c>LIKE</c> over <c>Content</c> and on the
/// SQLite test host falls back to a case-sensitive <c>Contains</c>. These cover the
/// common ACTIVE (non-recycle-bin) referencing note — the soft-deleted variants live in
/// <see cref="ReferenceCleanupRecycleBinTests"/> — so the refactor is proven not to have
/// changed which notes the scan reaches.
/// </summary>
public class ReferenceScanTests
{
    /// <summary>Inserts a note with exact raw content, bypassing the service sanitizer so the
    /// reference markup under test is preserved verbatim.</summary>
    private static async Task<NoteItem> AddNoteAsync(TestHost h, string title, string content)
    {
        var note = new NoteItem { Title = title, Content = content, ContentText = content };
        h.Db.Notes.Add(note);
        await h.Db.SaveChangesAsync();
        return note;
    }

    [Fact]
    public async Task TitleChange_UpdatesPageLink_InActiveReferencingNote()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Old Title");

        var pageLink =
            $"<div data-type=\"pageLink\" data-note-id=\"{target.Id}\" " +
            $"data-title=\"Old Title\">\U0001F4C4 Old Title</div>";
        var referencing = await AddNoteAsync(h, "Has page link", pageLink);

        var (updated, conflict, archived) = await h.Notes.Update(
            target.Id,
            new NoteItem { Title = "New Title", Content = target.Content, UpdatedAt = default },
            forceSave: true);
        Assert.NotNull(updated);
        Assert.False(conflict);
        Assert.False(archived);

        var reloaded = await h.FindAsync(referencing.Id);
        Assert.NotNull(reloaded);
        Assert.Contains("New Title", reloaded!.Content);
        Assert.DoesNotContain("Old Title", reloaded.Content);
    }

    [Fact]
    public async Task PermanentDelete_StripsMentions_FromActiveReferencingNote()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");

        var mention =
            $"<p><a data-type=\"noteMention\" data-note-id=\"{target.Id}\" " +
            $"data-title=\"Target\">@Target</a></p>";
        var referencing = await AddNoteAsync(h, "Mentions target", mention);

        Assert.True(await h.Notes.Delete(target.Id));
        var (found, wasInBin) = await h.Notes.DeletePermanent(target.Id);
        Assert.True(found);
        Assert.True(wasInBin);

        var reloaded = await h.FindAsync(referencing.Id);
        Assert.NotNull(reloaded);
        Assert.DoesNotContain($"data-note-id=\"{target.Id}\"", reloaded!.Content);
        // The plain "@Target" text remains (the link wrapper is stripped, not the text).
        Assert.Contains("@Target", reloaded.Content);
    }

    [Fact]
    public async Task Probe_DoesNotMatch_NoteWithoutReference()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");
        // A note that merely contains the raw GUID text but no data-note-id attribute
        // must not be treated as a reference.
        await AddNoteAsync(h, "Bare guid text", $"<p>see {target.Id} for details</p>");

        Assert.Empty(await h.Notes.GetBacklinks(target.Id));
    }
}
