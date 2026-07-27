using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression tests for issue #305: ancestor (<see cref="Vanadium.Note.REST.Services.NoteService.HasCircularReference"/>)
/// and descendant traversal were converted from per-level BFS round trips to a single recursive CTE.
/// These lock in that the CTE produces the same results as the old BFS across DEEP (multi-level) trees —
/// the whole point of the change was collapsing the per-level loop, so single-level coverage is not enough.
/// The CTE is standard SQL exercised here on the in-memory SQLite host; production runs the same SQL on PostgreSQL.
/// </summary>
public class RecursiveTraversalTests
{
    /// <summary>Builds a linear chain root → n1 → n2 → … of the given depth and returns every node, root first.</summary>
    private static async Task<List<NoteItem>> CreateChainAsync(TestHost h, int depth)
    {
        var chain = new List<NoteItem> { await h.CreateNoteAsync("root") };
        for (var i = 1; i < depth; i++)
            chain.Add(await h.CreateNoteAsync($"n{i}", chain[^1].Id));
        return chain;
    }

    // ── HasCircularReference over a multi-level ancestor chain ────────────────

    [Fact]
    public async Task HasCircularReference_ProposedParentIsDeepDescendant_DetectsCycle()
    {
        using var h = new TestHost();
        var chain = await CreateChainAsync(h, 5); // root, n1, n2, n3, n4

        // Reparenting root under its own deep descendant n4 would close a cycle:
        // the ancestor chain of n4 (n4→n3→n2→n1→root) contains root.
        Assert.True(await h.Notes.HasCircularReference(chain[0].Id, chain[4].Id));
    }

    [Fact]
    public async Task HasCircularReference_UnrelatedProposedParent_NoCycle()
    {
        using var h = new TestHost();
        var chain = await CreateChainAsync(h, 5);

        // Reparenting the deep leaf n4 under root is a normal move — root has no ancestors,
        // so n4 never appears above it.
        Assert.False(await h.Notes.HasCircularReference(chain[4].Id, chain[0].Id));
    }

    [Fact]
    public async Task HasCircularReference_SelfParent_IsCycle()
    {
        using var h = new TestHost();
        var root = await h.CreateNoteAsync("root");
        Assert.True(await h.Notes.HasCircularReference(root.Id, root.Id));
    }

    [Fact]
    public async Task HasCircularReference_StopsAtSoftDeletedAncestor()
    {
        using var h = new TestHost();
        var chain = await CreateChainAsync(h, 4); // root, n1, n2
        // Soft-delete the middle link n1. The old BFS walked db.Notes (global filter), so it
        // stopped at a soft-deleted ancestor; the CTE follows only non-deleted notes to match.
        Assert.True(await h.Notes.Delete(chain[1].Id));

        // Ancestors of n2 above the deleted n1 (i.e. root) are no longer reachable, so no cycle
        // is reported even though root is structurally above n2.
        Assert.False(await h.Notes.HasCircularReference(chain[0].Id, chain[2].Id));
    }

    // ── Descendant sweep over a deep chain (CollectActiveDescendants) ─────────

    [Fact]
    public async Task Delete_SoftDeletesEntireDeepSubtree()
    {
        using var h = new TestHost();
        var chain = await CreateChainAsync(h, 6); // 6 levels

        Assert.True(await h.Notes.Delete(chain[0].Id));

        // Every level, not just the first, is swept into the recycle bin.
        foreach (var node in chain)
        {
            var reloaded = await h.FindAsync(node.Id);
            Assert.NotNull(reloaded!.DeletedAt);
        }
    }

    // ── Archive group descent stops at an independently-archived subtree ──────

    [Fact]
    public async Task Unarchive_DeepGroup_DoesNotSweepIndependentlyArchivedSubtree()
    {
        using var h = new TestHost();
        // root → a → b → c (deep chain)
        var chain = await CreateChainAsync(h, 4);

        // Archive the lower subtree (b) on its own first — it gets its own archive group.
        Assert.True(await h.Notes.Archive(chain[2].Id));
        var bGroupBefore = (await h.FindAsync(chain[2].Id))!.ArchiveGroupId;

        // Now archive the root. Its sweep only touches still-active descendants (a),
        // leaving b/c in their independent group.
        Assert.True(await h.Notes.Archive(chain[0].Id));

        // Unarchiving root restores only the root group (root, a). The same-group CTE filter
        // must NOT descend into b/c, so they stay archived under their original group.
        Assert.True(await h.Notes.Unarchive(chain[0].Id));

        Assert.Null((await h.FindAsync(chain[0].Id))!.ArchivedAt); // root restored
        Assert.Null((await h.FindAsync(chain[1].Id))!.ArchivedAt); // a restored
        Assert.NotNull((await h.FindAsync(chain[2].Id))!.ArchivedAt); // b still archived
        Assert.NotNull((await h.FindAsync(chain[3].Id))!.ArchivedAt); // c still archived
        Assert.Equal(bGroupBefore, (await h.FindAsync(chain[2].Id))!.ArchiveGroupId);
    }
}
