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
        var user = await h.CreateUserAsync();
        var root = await h.CreateNoteAsync(user.Id, "Root");
        var child1 = await h.CreateNoteAsync(user.Id, "Child 1", root.Id);
        var child2 = await h.CreateNoteAsync(user.Id, "Child 2", root.Id);

        Assert.True(await h.Notes.Archive(user.Id, root.Id));

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
        var paged = await h.Notes.GetPaged(user.Id, 1, 30, null, "date", "desc", null);
        Assert.DoesNotContain(paged.Items, i => i.Id == root.Id);
        var summaries = await h.Notes.GetAllSummaries(user.Id);
        Assert.Empty(summaries);

        // Listed as a single root in the archive, with the group child count.
        var archive = await h.Notes.GetArchive(user.Id, 1, 30);
        var item = Assert.Single(archive.Items);
        Assert.Equal(root.Id, item.Id);
        Assert.Equal(2, item.ChildCount);
    }

    // ── T-2: unarchive restores the group with hierarchy intact ──────────────

    [Fact]
    public async Task Unarchive_RestoresGroup_HierarchyIntact()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var root = await h.CreateNoteAsync(user.Id, "Root");
        var child1 = await h.CreateNoteAsync(user.Id, "Child 1", root.Id);
        var child2 = await h.CreateNoteAsync(user.Id, "Child 2", root.Id);
        await h.Notes.Archive(user.Id, root.Id);

        Assert.True(await h.Notes.Unarchive(user.Id, root.Id));

        foreach (var id in new[] { root.Id, child1.Id, child2.Id })
        {
            var n = await h.FindAsync(id);
            Assert.Null(n!.ArchivedAt);
            Assert.False(n.IsArchiveRoot);
        }
        Assert.Equal(root.Id, (await h.FindAsync(child1.Id))!.ParentNoteId);

        var paged = await h.Notes.GetPaged(user.Id, 1, 30, null, "date", "desc", null);
        Assert.Contains(paged.Items, i => i.Id == root.Id);
    }

    // ── T-3: visibility — hidden from default reads (search half is PG-only) ─

    [Fact]
    public async Task ArchivedNote_ExcludedFromListChildrenAndMentions()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var parent = await h.CreateNoteAsync(user.Id, "Parent");
        var archivedChild = await h.CreateNoteAsync(user.Id, "Archived child", parent.Id);
        var activeChild = await h.CreateNoteAsync(user.Id, "Active child", parent.Id);
        await h.Notes.Archive(user.Id, archivedChild.Id);

        var children = await h.Notes.GetChildren(user.Id, parent.Id);
        Assert.Single(children);
        Assert.Equal(activeChild.Id, children[0].Id);

        var mentions = await h.Notes.SearchForMention(user.Id, string.Empty);
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
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Keep me");
        await h.Notes.Archive(user.Id, note.Id);

        var fetched = await h.Notes.Get(user.Id, note.Id);

        Assert.NotNull(fetched);
        Assert.NotNull(fetched!.ArchivedAt);
    }

    // ── T-5: idempotent archive ───────────────────────────────────────────────

    [Fact]
    public async Task Archive_AlreadyArchived_IsNoOp()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Once");
        await h.Notes.Archive(user.Id, note.Id);
        var firstTimestamp = (await h.FindAsync(note.Id))!.ArchivedAt;

        Assert.True(await h.Notes.Archive(user.Id, note.Id));

        var n = await h.FindAsync(note.Id);
        Assert.Equal(firstTimestamp, n!.ArchivedAt);
        Assert.True(n.IsArchiveRoot);
    }

    // ── T-6 / E3: nested archive groups stay independent ─────────────────────

    [Fact]
    public async Task Archive_OverArchivedSubtree_GroupsStayIndependent_OuterFirstUnarchive()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var root = await h.CreateNoteAsync(user.Id, "Root");
        var mid = await h.CreateNoteAsync(user.Id, "Mid", root.Id);
        var leaf = await h.CreateNoteAsync(user.Id, "Leaf", mid.Id);

        await h.Notes.Archive(user.Id, mid.Id);   // inner group: mid + leaf
        await h.Notes.Archive(user.Id, root.Id);  // outer group: root only

        var innerTimestamp = (await h.FindAsync(mid.Id))!.ArchivedAt;
        Assert.NotEqual(innerTimestamp, (await h.FindAsync(root.Id))!.ArchivedAt);
        Assert.True((await h.FindAsync(mid.Id))!.IsArchiveRoot);

        // Both groups appear independently in the archive list.
        var archive = await h.Notes.GetArchive(user.Id, 1, 30);
        Assert.Equal(2, archive.TotalCount);

        // Unarchive the outer root: inner subtree stays archived.
        await h.Notes.Unarchive(user.Id, root.Id);
        Assert.Null((await h.FindAsync(root.Id))!.ArchivedAt);
        Assert.Equal(innerTimestamp, (await h.FindAsync(mid.Id))!.ArchivedAt);
        Assert.Equal(innerTimestamp, (await h.FindAsync(leaf.Id))!.ArchivedAt);

        // Later inner unarchive finds its parent active and keeps the hierarchy.
        await h.Notes.Unarchive(user.Id, mid.Id);
        var m = await h.FindAsync(mid.Id);
        Assert.Null(m!.ArchivedAt);
        Assert.Equal(root.Id, m.ParentNoteId);
        Assert.Null((await h.FindAsync(leaf.Id))!.ArchivedAt);
    }

    [Fact]
    public async Task Archive_OverArchivedSubtree_InnerFirstUnarchive_ReattachesToRoot()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var root = await h.CreateNoteAsync(user.Id, "Root");
        var mid = await h.CreateNoteAsync(user.Id, "Mid", root.Id);
        var leaf = await h.CreateNoteAsync(user.Id, "Leaf", mid.Id);

        await h.Notes.Archive(user.Id, mid.Id);
        await h.Notes.Archive(user.Id, root.Id);

        // Inner-first: the parent (root) is still archived → detach to root note.
        await h.Notes.Unarchive(user.Id, mid.Id);

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
        var user = await h.CreateUserAsync();
        var root = await h.CreateNoteAsync(user.Id, "Root");
        var child = await h.CreateNoteAsync(user.Id, "Deleted child", root.Id);
        await h.Notes.Delete(user.Id, child.Id);

        await h.Notes.Archive(user.Id, root.Id);

        var c = await h.FindAsync(child.Id);
        Assert.Null(c!.ArchivedAt);       // sweep skipped it
        Assert.NotNull(c.DeletedAt);      // still in the recycle bin

        // Restoring it later: parent is archived → re-attach as root note (E2).
        Assert.True(await h.Notes.Restore(user.Id, child.Id));
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
        var user = await h.CreateUserAsync();
        var parent = await h.CreateNoteAsync(user.Id, "Parent");
        var note = await h.CreateNoteAsync(user.Id, "Archived", parent.Id);
        await h.Notes.Archive(user.Id, note.Id);

        // Simulate the parent ending up soft-deleted without sweeping the child
        // (direct state manipulation: the guard must hold for any historical data).
        await h.Db.Notes.IgnoreQueryFilters()
            .Where(n => n.Id == parent.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DeletedAt, DateTime.UtcNow)
                .SetProperty(n => n.IsDeletionRoot, true));

        Assert.True(await h.Notes.Unarchive(user.Id, note.Id));

        var n = await h.FindAsync(note.Id);
        Assert.Null(n!.ArchivedAt);
        Assert.Null(n.ParentNoteId);
    }

    // ── T-9: write paths are rejected for archived notes ─────────────────────

    [Fact]
    public async Task Update_OnArchivedNote_ReturnsArchivedSignal()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Read-only");
        await h.Notes.Archive(user.Id, note.Id);

        var (updated, conflict, archived) = await h.Notes.Update(user.Id, note.Id,
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
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Labelled");
        var label = await h.Labels.CreateLabelAsync(user.Id, "todo", null);
        await h.Labels.AddLabelToNoteAsync(user.Id, note.Id, label.Id);
        await h.Notes.Archive(user.Id, note.Id);

        var other = await h.Labels.CreateLabelAsync(user.Id, "later", null);

        await Assert.ThrowsAsync<LabelService.NoteArchivedException>(() =>
            h.Labels.AddLabelToNoteAsync(user.Id, note.Id, other.Id));
        await Assert.ThrowsAsync<LabelService.NoteArchivedException>(() =>
            h.Labels.RemoveLabelFromNoteAsync(user.Id, note.Id, label.Id));
    }

    // ── T-10: wrong-state and cross-user targets ─────────────────────────────

    [Fact]
    public async Task Archive_RecycleBinnedNote_NotFound()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Binned");
        await h.Notes.Delete(user.Id, note.Id);

        Assert.False(await h.Notes.Archive(user.Id, note.Id));
    }

    [Fact]
    public async Task Unarchive_ActiveNote_NotFound()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Active");

        Assert.False(await h.Notes.Unarchive(user.Id, note.Id));
    }

    [Fact]
    public async Task ArchiveAndUnarchive_CrossUser_NotFound()
    {
        using var h = new TestHost();
        var owner = await h.CreateUserAsync("owner");
        var intruder = await h.CreateUserAsync("intruder");
        var note = await h.CreateNoteAsync(owner.Id, "Private");

        Assert.False(await h.Notes.Archive(intruder.Id, note.Id));
        await h.Notes.Archive(owner.Id, note.Id);
        Assert.False(await h.Notes.Unarchive(intruder.Id, note.Id));
    }

    // ── T-11 / E9: attachments of archived notes are never orphans ───────────

    [Fact]
    public async Task OrphanScan_KeepsArchivedNoteAttachments_RemovesAfterPermanentDelete()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var attachment = new FileAttachment { OriginalName = "spec.pdf", ContentType = "application/pdf" };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();
        var physicalPath = Path.Combine(h.ContentRoot, "uploads", $"file_{attachment.Id}");
        await File.WriteAllTextAsync(physicalPath, "payload");

        var note = await h.CreateNoteAsync(user.Id, "With file",
            content: $"<p><a class=\"file-attachment\" href=\"/api/files/{attachment.Id}\">spec.pdf</a></p>");
        await h.Notes.Archive(user.Id, note.Id);

        // Archived content counts as a live reference: nothing is collected.
        await h.FileCleanup.DeleteAllOrphansAsync();
        Assert.True(File.Exists(physicalPath));
        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));

        // Recycle bin → permanent delete: now the file goes away.
        await h.Notes.Delete(user.Id, note.Id);
        await h.Notes.DeletePermanent(user.Id, note.Id);
        await h.FileCleanup.DeleteAllOrphansAsync();
        Assert.False(File.Exists(physicalPath));
        Assert.Null(await h.Db.FileAttachments.FindAsync(attachment.Id));
    }

    // ── T-12 / E10: account wipe removes archived notes too ──────────────────

    [Fact]
    public async Task AccountWipe_RemovesActiveArchivedAndDeletedNotes()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync("wipe-me");
        await h.CreateNoteAsync(user.Id, "Active");
        var archived = await h.CreateNoteAsync(user.Id, "Archived");
        var deleted = await h.CreateNoteAsync(user.Id, "Deleted");
        await h.Notes.Archive(user.Id, archived.Id);
        await h.Notes.Delete(user.Id, deleted.Id);

        Assert.True(await h.Account.PurgeAllDataAsync("wipe-me"));

        Assert.Equal(0, await h.Db.Notes.IgnoreQueryFilters().CountAsync(n => n.UserId == user.Id));
    }

    // ── T-13 / FR-5 / UC-5: recycle bin round-trip preserves archive state ───

    [Fact]
    public async Task DeleteArchivedRoot_RestoreReturnsItToArchive()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var root = await h.CreateNoteAsync(user.Id, "Archived tree");
        var child = await h.CreateNoteAsync(user.Id, "Child", root.Id);
        await h.Notes.Archive(user.Id, root.Id);
        var archivedAt = (await h.FindAsync(root.Id))!.ArchivedAt;

        // Move to the recycle bin: ArchivedAt is kept, archive list hides it.
        Assert.True(await h.Notes.Delete(user.Id, root.Id));
        var binned = await h.FindAsync(root.Id);
        Assert.NotNull(binned!.DeletedAt);
        Assert.Equal(archivedAt, binned.ArchivedAt);
        Assert.NotNull((await h.FindAsync(child.Id))!.DeletedAt);
        Assert.Empty((await h.Notes.GetArchive(user.Id, 1, 30)).Items);

        // The recycle bin flags it as archived (UI badge: restore goes to the Archive).
        var bin = await h.Notes.GetRecycleBin(user.Id, 1, 30);
        var binItem = Assert.Single(bin.Items);
        Assert.True(binItem.IsArchived);

        // Restore: back to the archive, not the active list (lossless).
        Assert.True(await h.Notes.Restore(user.Id, root.Id));
        var restored = await h.FindAsync(root.Id);
        Assert.Null(restored!.DeletedAt);
        Assert.Equal(archivedAt, restored.ArchivedAt);
        Assert.True(restored.IsArchiveRoot);

        var archive = await h.Notes.GetArchive(user.Id, 1, 30);
        Assert.Single(archive.Items);
        var paged = await h.Notes.GetPaged(user.Id, 1, 30, null, "date", "desc", null);
        Assert.DoesNotContain(paged.Items, i => i.Id == root.Id);
    }

    [Fact]
    public async Task GetRecycleBin_FlagsOnlyArchivedNotes()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var plain = await h.CreateNoteAsync(user.Id, "Plain");
        var archived = await h.CreateNoteAsync(user.Id, "Archived");
        await h.Notes.Archive(user.Id, archived.Id);
        await h.Notes.Delete(user.Id, plain.Id);
        await h.Notes.Delete(user.Id, archived.Id);

        var bin = await h.Notes.GetRecycleBin(user.Id, 1, 30);

        Assert.Equal(2, bin.TotalCount);
        Assert.False(bin.Items.Single(i => i.Id == plain.Id).IsArchived);
        Assert.True(bin.Items.Single(i => i.Id == archived.Id).IsArchived);
    }

    // ── T-14 / E8: no permanent-delete shortcut from the archive ─────────────

    [Fact]
    public async Task DeletePermanent_OnArchivedButNotDeletedNote_Refused()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Archived");
        await h.Notes.Archive(user.Id, note.Id);

        var (found, wasInRecycleBin) = await h.Notes.DeletePermanent(user.Id, note.Id);

        Assert.True(found);
        Assert.False(wasInRecycleBin);
        Assert.NotNull(await h.FindAsync(note.Id));
    }

    // ── FR-8 / E1: no new children or re-parenting under archived notes ──────

    [Fact]
    public async Task Restore_ActiveNoteUnderArchivedParent_Detaches()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var parent = await h.CreateNoteAsync(user.Id, "Parent");
        var child = await h.CreateNoteAsync(user.Id, "Child", parent.Id);

        await h.Notes.Delete(user.Id, child.Id);   // child deleted while active
        await h.Notes.Archive(user.Id, parent.Id); // parent archived meanwhile

        Assert.True(await h.Notes.Restore(user.Id, child.Id));

        var c = await h.FindAsync(child.Id);
        Assert.Null(c!.DeletedAt);
        Assert.Null(c.ParentNoteId); // would otherwise resurrect under a read-only parent
    }
}
