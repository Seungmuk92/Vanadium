using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Service-level tests for the Note Archive feature (spec T-1 .. T-14).
/// PostgreSQL-only behavior (trigram ILIKE search inclusion, T-3 search half)
/// is covered by manual Swagger/UI verification, not unit tests.
/// </summary>
public class ArchiveServiceTests
{
    // ── T-1: archive sweeps active descendants into one group ────────────────

    [Fact]
    public async Task Archive_SweepsActiveDescendants_SharedTimestampAndRootFlag()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("Root");
        var child1 = await h.CreateNoteAsync("Child 1", root.Id);
        var child2 = await h.CreateNoteAsync("Child 2", root.Id);

        Assert.True(await h.Notes.Archive(root.Id));

        var r = await h.FindAsync(root.Id);
        var c1 = await h.FindAsync(child1.Id);
        var c2 = await h.FindAsync(child2.Id);

        Assert.NotNull(r!.ArchivedAt);
        Assert.Equal(r.ArchivedAt, c1!.ArchivedAt);
        Assert.Equal(r.ArchivedAt, c2!.ArchivedAt);
        Assert.True(r.IsArchiveRoot);
        Assert.False(c1.IsArchiveRoot);
        Assert.False(c2.IsArchiveRoot);

        // Hidden from the Home list (non-search) and the Board summaries.
        var paged = await h.Notes.GetPaged(1, 30, null, "date", "desc", null);
        Assert.DoesNotContain(paged.Items, i => i.Id == root.Id);
        var summaries = await h.Notes.GetAllSummaries();
        Assert.Empty(summaries);

        // Listed as a single root in the archive, with the group child count.
        var archive = await h.Notes.GetArchive(1, 30);
        var item = Assert.Single(archive.Items);
        Assert.Equal(root.Id, item.Id);
        Assert.Equal(2, item.ChildCount);
    }

    // ── T-2: unarchive restores the group with hierarchy intact ──────────────

    [Fact]
    public async Task Unarchive_RestoresGroup_HierarchyIntact()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("Root");
        var child1 = await h.CreateNoteAsync("Child 1", root.Id);
        var child2 = await h.CreateNoteAsync("Child 2", root.Id);
        await h.Notes.Archive(root.Id);

        Assert.True(await h.Notes.Unarchive(root.Id));

        foreach (var id in new[] { root.Id, child1.Id, child2.Id })
        {
            var n = await h.FindAsync(id);
            Assert.Null(n!.ArchivedAt);
            Assert.False(n.IsArchiveRoot);
        }
        Assert.Equal(root.Id, (await h.FindAsync(child1.Id))!.ParentNoteId);

        var paged = await h.Notes.GetPaged(1, 30, null, "date", "desc", null);
        Assert.Contains(paged.Items, i => i.Id == root.Id);
    }

    // ── T-3: visibility — hidden from default reads (search half is PG-only) ─

    [Fact]
    public async Task ArchivedNote_ExcludedFromListChildrenAndMentions()
    {
        using var h = new TestHost();
        var parent = await h.CreateNoteAsync("Parent");
        var archivedChild = await h.CreateNoteAsync("Archived child", parent.Id);
        var activeChild = await h.CreateNoteAsync("Active child", parent.Id);
        await h.Notes.Archive(archivedChild.Id);

        var children = await h.Notes.GetChildren(parent.Id);
        Assert.Single(children);
        Assert.Equal(activeChild.Id, children[0].Id);

        var mentions = await h.Notes.SearchForMention(string.Empty);
        Assert.DoesNotContain(mentions, m => m.Id == archivedChild.Id);
        Assert.Contains(mentions, m => m.Id == parent.Id);
    }

    [Fact(Skip = "Search inclusion uses EF.Functions.ILike (PostgreSQL trigram); verified manually via Swagger/UI.")]
    public Task Search_IncludesArchivedNotes_WithIsArchivedFlag() => Task.CompletedTask;

    // ── T-4: GET stays allowed ────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsArchivedNote_WithArchivedAt()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Keep me");
        await h.Notes.Archive(note.Id);

        var fetched = await h.Notes.Get(note.Id);

        Assert.NotNull(fetched);
        Assert.NotNull(fetched!.ArchivedAt);
    }

    // ── T-5: idempotent archive ───────────────────────────────────────────────

    [Fact]
    public async Task Archive_AlreadyArchived_IsNoOp()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Once");
        await h.Notes.Archive(note.Id);
        var firstTimestamp = (await h.FindAsync(note.Id))!.ArchivedAt;

        Assert.True(await h.Notes.Archive(note.Id));

        var n = await h.FindAsync(note.Id);
        Assert.Equal(firstTimestamp, n!.ArchivedAt);
        Assert.True(n.IsArchiveRoot);
    }

    // ── T-6 / E3: nested archive groups stay independent ─────────────────────

    [Fact]
    public async Task Archive_OverArchivedSubtree_GroupsStayIndependent_OuterFirstUnarchive()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("Root");
        var mid = await h.CreateNoteAsync("Mid", root.Id);
        var leaf = await h.CreateNoteAsync("Leaf", mid.Id);

        await h.Notes.Archive(mid.Id);   // inner group: mid + leaf
        await h.Notes.Archive(root.Id);  // outer group: root only

        var innerTimestamp = (await h.FindAsync(mid.Id))!.ArchivedAt;
        Assert.NotEqual(innerTimestamp, (await h.FindAsync(root.Id))!.ArchivedAt);
        Assert.True((await h.FindAsync(mid.Id))!.IsArchiveRoot);

        // Both groups appear independently in the archive list.
        var archive = await h.Notes.GetArchive(1, 30);
        Assert.Equal(2, archive.TotalCount);

        // Unarchive the outer root: inner subtree stays archived.
        await h.Notes.Unarchive(root.Id);
        Assert.Null((await h.FindAsync(root.Id))!.ArchivedAt);
        Assert.Equal(innerTimestamp, (await h.FindAsync(mid.Id))!.ArchivedAt);
        Assert.Equal(innerTimestamp, (await h.FindAsync(leaf.Id))!.ArchivedAt);

        // Later inner unarchive finds its parent active and keeps the hierarchy.
        await h.Notes.Unarchive(mid.Id);
        var m = await h.FindAsync(mid.Id);
        Assert.Null(m!.ArchivedAt);
        Assert.Equal(root.Id, m.ParentNoteId);
        Assert.Null((await h.FindAsync(leaf.Id))!.ArchivedAt);
    }

    [Fact]
    public async Task Archive_OverArchivedSubtree_InnerFirstUnarchive_ReattachesToRoot()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("Root");
        var mid = await h.CreateNoteAsync("Mid", root.Id);
        var leaf = await h.CreateNoteAsync("Leaf", mid.Id);

        await h.Notes.Archive(mid.Id);
        await h.Notes.Archive(root.Id);

        // Inner-first: the parent (root) is still archived → detach to root note.
        await h.Notes.Unarchive(mid.Id);

        var m = await h.FindAsync(mid.Id);
        Assert.Null(m!.ArchivedAt);
        Assert.Null(m.ParentNoteId);
        Assert.Null((await h.FindAsync(leaf.Id))!.ArchivedAt);
        Assert.NotNull((await h.FindAsync(root.Id))!.ArchivedAt);
    }

    // ── T-7 / E4: recycle-binned descendants are skipped by the sweep ────────

    [Fact]
    public async Task Archive_SkipsRecycleBinnedDescendant_RestoreDetachesIt()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("Root");
        var child = await h.CreateNoteAsync("Deleted child", root.Id);
        await h.Notes.Delete(child.Id);

        await h.Notes.Archive(root.Id);

        var c = await h.FindAsync(child.Id);
        Assert.Null(c!.ArchivedAt);       // sweep skipped it
        Assert.NotNull(c.DeletedAt);      // still in the recycle bin

        // Restoring it later: parent is archived → re-attach as root note (E2).
        Assert.True(await h.Notes.Restore(child.Id));
        c = await h.FindAsync(child.Id);
        Assert.Null(c!.DeletedAt);
        Assert.Null(c.ArchivedAt);
        Assert.Null(c.ParentNoteId);
    }

    // ── T-8: unarchive when the parent disappeared meanwhile ─────────────────

    [Fact]
    public async Task Unarchive_ParentSoftDeletedMeanwhile_ReattachesToRoot()
    {
        using var h = new TestHost();
        var parent = await h.CreateNoteAsync("Parent");
        var note = await h.CreateNoteAsync("Archived", parent.Id);
        await h.Notes.Archive(note.Id);

        // Simulate the parent ending up soft-deleted without sweeping the child
        // (direct state manipulation: the guard must hold for any historical data).
        await h.Db.Notes.IgnoreQueryFilters()
            .Where(n => n.Id == parent.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DeletedAt, DateTime.UtcNow)
                .SetProperty(n => n.IsDeletionRoot, true));

        Assert.True(await h.Notes.Unarchive(note.Id));

        var n = await h.FindAsync(note.Id);
        Assert.Null(n!.ArchivedAt);
        Assert.Null(n.ParentNoteId);
    }

    // ── T-9: write paths are rejected for archived notes ─────────────────────

    [Fact]
    public async Task Update_OnArchivedNote_ReturnsArchivedSignal()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Read-only");
        await h.Notes.Archive(note.Id);

        var (updated, conflict, archived) = await h.Notes.Update(note.Id,
            new NoteItem { Title = "Changed", Content = "", UpdatedAt = default });

        Assert.Null(updated);
        Assert.False(conflict);
        Assert.True(archived);
        Assert.Equal("Read-only", (await h.FindAsync(note.Id))!.Title);
    }

    [Fact]
    public async Task LabelMutations_OnArchivedNote_Throw()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Labelled");
        var label = await h.Labels.CreateLabelAsync("todo", null);
        await h.Labels.AddLabelToNoteAsync(note.Id, label.Id);
        await h.Notes.Archive(note.Id);

        var other = await h.Labels.CreateLabelAsync("later", null);

        await Assert.ThrowsAsync<LabelService.NoteArchivedException>(() =>
            h.Labels.AddLabelToNoteAsync(note.Id, other.Id));
        await Assert.ThrowsAsync<LabelService.NoteArchivedException>(() =>
            h.Labels.RemoveLabelFromNoteAsync(note.Id, label.Id));
    }

    // ── T-10: wrong-state targets ─────────────────────────────────────────────

    [Fact]
    public async Task Archive_RecycleBinnedNote_NotFound()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Binned");
        await h.Notes.Delete(note.Id);

        Assert.False(await h.Notes.Archive(note.Id));
    }

    [Fact]
    public async Task Unarchive_ActiveNote_NotFound()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Active");

        Assert.False(await h.Notes.Unarchive(note.Id));
    }

    // ── T-11 / E9: attachments of archived notes are never orphans ───────────

    [Fact]
    public async Task OrphanScan_KeepsArchivedNoteAttachments_RemovesAfterPermanentDelete()
    {
        using var h = new TestHost();

        var attachment = new FileAttachment { OriginalName = "spec.pdf", ContentType = "application/pdf" };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();
        var physicalPath = Path.Combine(h.ContentRoot, "uploads", $"file_{attachment.Id}");
        await File.WriteAllTextAsync(physicalPath, "payload");

        var note = await h.CreateNoteAsync("With file",
            content: $"<p><a class=\"file-attachment\" href=\"/api/files/{attachment.Id}\">spec.pdf</a></p>");
        await h.Notes.Archive(note.Id);

        // Archived content counts as a live reference: nothing is collected.
        await h.FileCleanup.DeleteAllOrphansAsync();
        Assert.True(File.Exists(physicalPath));
        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));

        // Recycle bin → permanent delete: now the file goes away.
        await h.Notes.Delete(note.Id);
        await h.Notes.DeletePermanent(note.Id);
        await h.FileCleanup.DeleteAllOrphansAsync();
        Assert.False(File.Exists(physicalPath));
        Assert.Null(await h.Db.FileAttachments.FindAsync(attachment.Id));
    }

    // ── T-12 / E10: account wipe removes archived notes too ──────────────────

    [Fact]
    public async Task AccountWipe_RemovesActiveArchivedAndDeletedNotes()
    {
        using var h = new TestHost();
        await h.CreateNoteAsync("Active");
        var archived = await h.CreateNoteAsync("Archived");
        var deleted = await h.CreateNoteAsync("Deleted");
        await h.Notes.Archive(archived.Id);
        await h.Notes.Delete(deleted.Id);

        Assert.True(await h.Account.PurgeAllDataAsync());

        Assert.Equal(0, await h.Db.Notes.IgnoreQueryFilters().CountAsync());
    }

    // ── T-13 / FR-5 / UC-5: recycle bin round-trip preserves archive state ───

    [Fact]
    public async Task DeleteArchivedRoot_RestoreReturnsItToArchive()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("Archived tree");
        var child = await h.CreateNoteAsync("Child", root.Id);
        await h.Notes.Archive(root.Id);
        var archivedAt = (await h.FindAsync(root.Id))!.ArchivedAt;

        // Move to the recycle bin: ArchivedAt is kept, archive list hides it.
        Assert.True(await h.Notes.Delete(root.Id));
        var binned = await h.FindAsync(root.Id);
        Assert.NotNull(binned!.DeletedAt);
        Assert.Equal(archivedAt, binned.ArchivedAt);
        Assert.NotNull((await h.FindAsync(child.Id))!.DeletedAt);
        Assert.Empty((await h.Notes.GetArchive(1, 30)).Items);

        // The recycle bin flags it as archived (UI badge: restore goes to the Archive).
        var bin = await h.Notes.GetRecycleBin(1, 30);
        var binItem = Assert.Single(bin.Items);
        Assert.True(binItem.IsArchived);

        // Restore: back to the archive, not the active list (lossless).
        Assert.True(await h.Notes.Restore(root.Id));
        var restored = await h.FindAsync(root.Id);
        Assert.Null(restored!.DeletedAt);
        Assert.Equal(archivedAt, restored.ArchivedAt);
        Assert.True(restored.IsArchiveRoot);

        var archive = await h.Notes.GetArchive(1, 30);
        Assert.Single(archive.Items);
        var paged = await h.Notes.GetPaged(1, 30, null, "date", "desc", null);
        Assert.DoesNotContain(paged.Items, i => i.Id == root.Id);
    }

    [Fact]
    public async Task GetRecycleBin_FlagsOnlyArchivedNotes()
    {
        using var h = new TestHost();
        var plain = await h.CreateNoteAsync("Plain");
        var archived = await h.CreateNoteAsync("Archived");
        await h.Notes.Archive(archived.Id);
        await h.Notes.Delete(plain.Id);
        await h.Notes.Delete(archived.Id);

        var bin = await h.Notes.GetRecycleBin(1, 30);

        Assert.Equal(2, bin.TotalCount);
        Assert.False(bin.Items.Single(i => i.Id == plain.Id).IsArchived);
        Assert.True(bin.Items.Single(i => i.Id == archived.Id).IsArchived);
    }

    // ── T-14 / E8: no permanent-delete shortcut from the archive ─────────────

    [Fact]
    public async Task DeletePermanent_OnArchivedButNotDeletedNote_Refused()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Archived");
        await h.Notes.Archive(note.Id);

        var (found, wasInRecycleBin) = await h.Notes.DeletePermanent(note.Id);

        Assert.True(found);
        Assert.False(wasInRecycleBin);
        Assert.NotNull(await h.FindAsync(note.Id));
    }

    // ── FR-8 / E1: no new children or re-parenting under archived notes ──────

    [Fact]
    public async Task Restore_ActiveNoteUnderArchivedParent_Detaches()
    {
        using var h = new TestHost();
        var parent = await h.CreateNoteAsync("Parent");
        var child = await h.CreateNoteAsync("Child", parent.Id);

        await h.Notes.Delete(child.Id);   // child deleted while active
        await h.Notes.Archive(parent.Id); // parent archived meanwhile

        Assert.True(await h.Notes.Restore(child.Id));

        var c = await h.FindAsync(child.Id);
        Assert.Null(c!.DeletedAt);
        Assert.Null(c.ParentNoteId); // would otherwise resurrect under a read-only parent
    }
}
