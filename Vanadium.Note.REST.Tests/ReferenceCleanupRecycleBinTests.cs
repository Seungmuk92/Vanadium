using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression tests for issue #115: mention stripping and page-link title
/// propagation must reach recycle-bin (soft-deleted) referencing notes, so a
/// restored note never carries a dead mention or a stale page-link title.
/// </summary>
public class ReferenceCleanupRecycleBinTests
{
    /// <summary>Inserts a soft-deleted note with exact content, bypassing the
    /// service's sanitizer so the reference markup under test is preserved.</summary>
    private static async Task<NoteItem> AddSoftDeletedNoteAsync(TestHost h, string content)
    {
        var note = new NoteItem
        {
            Title = "Referencing (recycle bin)",
            Content = content,
            ContentText = content,
            DeletedAt = DateTime.UtcNow,
            IsDeletionRoot = true,
        };
        h.Db.Notes.Add(note);
        await h.Db.SaveChangesAsync();
        return note;
    }

    // ── Mention stripping reaches soft-deleted referencing notes ──────────────

    [Fact]
    public async Task PermanentDelete_StripsMentions_FromRecycleBinNote()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Target");

        var mention =
            $"<p><a data-type=\"noteMention\" data-note-id=\"{target.Id}\" " +
            $"data-title=\"Target\">@Target</a></p>";
        var referencing = await AddSoftDeletedNoteAsync(h, mention);

        // Soft-delete then permanently delete the target — this triggers mention stripping.
        Assert.True(await h.Notes.Delete(target.Id));
        var (found, wasInBin) = await h.Notes.DeletePermanent(target.Id);
        Assert.True(found);
        Assert.True(wasInBin);

        // The recycle-bin note no longer holds a dead mention link to the target.
        var reloaded = await h.FindAsync(referencing.Id);
        Assert.NotNull(reloaded);
        Assert.DoesNotContain($"data-note-id=\"{target.Id}\"", reloaded!.Content);
        // The plain "@Target" text remains (the link wrapper is stripped, not the text).
        Assert.Contains("@Target", reloaded.Content);
    }

    // ── Page-link title propagation reaches soft-deleted referencing notes ─────

    [Fact]
    public async Task TitleChange_UpdatesPageLink_InRecycleBinNote()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Old Title");

        var pageLink =
            $"<div data-type=\"pageLink\" data-note-id=\"{target.Id}\" " +
            $"data-title=\"Old Title\">\U0001F4C4 Old Title</div>";
        var referencing = await AddSoftDeletedNoteAsync(h, pageLink);

        // Rename the target (UpdatedAt=default → force-save, bypasses concurrency check).
        var (updated, conflict, archived) = await h.Notes.Update(
            target.Id,
            new NoteItem { Title = "New Title", Content = target.Content, UpdatedAt = default });
        Assert.NotNull(updated);
        Assert.False(conflict);
        Assert.False(archived);

        // The recycle-bin note's page-link now reflects the new title.
        var reloaded = await h.FindAsync(referencing.Id);
        Assert.NotNull(reloaded);
        Assert.Contains("New Title", reloaded!.Content);
        Assert.DoesNotContain("Old Title", reloaded.Content);
    }
}
