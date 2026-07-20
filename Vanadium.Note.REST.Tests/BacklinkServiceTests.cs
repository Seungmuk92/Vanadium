using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Service-level tests for the backlink ("what links here") query (issue #141).
/// The scan goes through the shared reference probe (<c>WhereReferencesNote</c>): an
/// index-friendly <c>LIKE</c> on PostgreSQL (issue #220) and a case-sensitive
/// <c>Content.Contains</c> fallback on the in-memory SQLite host, which is the branch
/// exercised here — so the visibility rules (soft-delete exclusion, archive inclusion,
/// self-exclusion) are verified directly.
/// </summary>
public class BacklinkServiceTests
{
    /// <summary>Inserts a note with exact raw content, bypassing the service sanitizer so the
    /// reference markup under test is preserved. Optionally soft-deleted or archived.</summary>
    private static async Task<NoteItem> AddNoteAsync(
        TestHost h, string title, string content, bool softDeleted = false, bool archived = false)
    {
        var note = new NoteItem
        {
            Title = title,
            Content = content,
            ContentText = content,
            DeletedAt = softDeleted ? DateTime.UtcNow : null,
            IsDeletionRoot = softDeleted,
            ArchivedAt = archived ? DateTime.UtcNow : null,
            IsArchiveRoot = archived,
        };
        h.Db.Notes.Add(note);
        await h.Db.SaveChangesAsync();
        return note;
    }

    private static string MentionMarkup(Guid targetId, string title = "Target") =>
        $"<p><a data-type=\"noteMention\" data-note-id=\"{targetId}\" data-title=\"{title}\">@{title}</a></p>";

    [Fact]
    public async Task GetBacklinks_ReturnsNotesReferencingTarget()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");
        var referencing = await AddNoteAsync(h, "Referencing", MentionMarkup(target.Id));
        await AddNoteAsync(h, "Unrelated", "<p>no links here</p>");

        var result = await h.Notes.GetBacklinks(target.Id);

        var hit = Assert.Single(result);
        Assert.Equal(referencing.Id, hit.Id);
        Assert.Equal("Referencing", hit.Title);
        Assert.False(hit.IsArchived);
    }

    [Fact]
    public async Task GetBacklinks_ExcludesSoftDeletedReferencingNotes()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");
        await AddNoteAsync(h, "In recycle bin", MentionMarkup(target.Id), softDeleted: true);

        var result = await h.Notes.GetBacklinks(target.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetBacklinks_IncludesArchivedReferencingNotes_Flagged()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");
        var archived = await AddNoteAsync(h, "Archived note", MentionMarkup(target.Id), archived: true);

        var result = await h.Notes.GetBacklinks(target.Id);

        var hit = Assert.Single(result);
        Assert.Equal(archived.Id, hit.Id);
        Assert.True(hit.IsArchived);
    }

    [Fact]
    public async Task GetBacklinks_ExcludesSelfReference()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");
        // A note that references its own id must not appear in its own backlinks.
        var self = await AddNoteAsync(h, "Self", MentionMarkup(target.Id));
        var selfRef = await AddNoteAsync(h, "Self-referencing", MentionMarkup(default));
        // Rewrite selfRef to reference its own id.
        selfRef.Content = MentionMarkup(selfRef.Id);
        selfRef.ContentText = selfRef.Content;
        await h.Db.SaveChangesAsync();

        var result = await h.Notes.GetBacklinks(selfRef.Id);

        Assert.DoesNotContain(result, r => r.Id == selfRef.Id);
    }

    [Fact]
    public async Task GetBacklinks_MatchesPageLinkMarkup()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");
        var pageLink =
            $"<div data-type=\"pageLink\" data-note-id=\"{target.Id}\" data-title=\"Target\">\U0001F4C4 Target</div>";
        var referencing = await AddNoteAsync(h, "Has page link", pageLink);

        var result = await h.Notes.GetBacklinks(target.Id);

        Assert.Contains(result, r => r.Id == referencing.Id);
    }

    [Fact]
    public async Task GetBacklinks_NoReferences_ReturnsEmpty()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Lonely");

        Assert.Empty(await h.Notes.GetBacklinks(target.Id));
    }
}
