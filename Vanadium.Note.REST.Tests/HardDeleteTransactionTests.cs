using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression tests for issue #116: <c>HardDeleteAsync</c>'s multi-save sequence
/// (parent page-link strip + mention stripping over the whole subtree + the note
/// removal) is wrapped in a single execution-strategy transaction, so it commits
/// atomically. These tests exercise the wrapped happy path across a descendant
/// subtree, guarding that the transaction wrapping did not regress the outcome.
/// (True rollback-on-failure is not asserted here — the current in-memory SQLite
/// harness offers no clean seam to inject a mid-sequence DB failure.)
/// </summary>
public class HardDeleteTransactionTests
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

    [Fact]
    public async Task PermanentDelete_WithSubtree_AtomicallyRemovesNotesAndStripsMentions()
    {
        using var h = new TestHost();

        // Parent with a child — the child is a descendant swept into the subtree.
        var parent = await h.CreateNoteAsync("Parent");
        var child = await h.CreateNoteAsync("Child", parentId: parent.Id);

        // A recycle-bin note mentions the child — stripping it happens inside the
        // subtree loop, i.e. within the transaction.
        var mention =
            $"<p><a data-type=\"noteMention\" data-note-id=\"{child.Id}\" " +
            $"data-title=\"Child\">@Child</a></p>";
        var referencing = await AddSoftDeletedNoteAsync(h, mention);

        // Soft-delete the parent (cascades the child into the bin), then purge it.
        Assert.True(await h.Notes.Delete(parent.Id));
        var (found, wasInBin) = await h.Notes.DeletePermanent(parent.Id);
        Assert.True(found);
        Assert.True(wasInBin);

        // Both parent and child are gone — the whole subtree committed.
        Assert.Null(await h.FindAsync(parent.Id));
        Assert.Null(await h.FindAsync(child.Id));

        // The recycle-bin note's dead mention to the child was stripped.
        var reloaded = await h.FindAsync(referencing.Id);
        Assert.NotNull(reloaded);
        Assert.DoesNotContain($"data-note-id=\"{child.Id}\"", reloaded!.Content);
        Assert.Contains("@Child", reloaded.Content);
    }
}
