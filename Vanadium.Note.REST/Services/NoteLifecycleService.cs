using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Owns the note lifecycle transitions (issue #311 split out of <see cref="NoteService"/>):
/// soft delete / restore (recycle bin), archive / unarchive, and permanent deletion (single, empty,
/// and the retention-cutoff purge). <see cref="NoteService"/> delegates its lifecycle methods here so
/// the public API — and the controller/test surface — is unchanged.
/// <para>
/// The <c>IgnoreQueryFilters()</c> opt-outs and the explicit archive-visibility predicates carry over
/// verbatim from the original service: soft-deleted notes stay visible to the recycle-bin and purge
/// paths and to reference stripping, while the default global filter keeps them out of everything else.
/// </para>
/// </summary>
public class NoteLifecycleService(
    NoteDbContext db,
    FileCleanupService fileCleanup,
    ILogger<NoteLifecycleService> logger)
{
    /// <summary>
    /// Soft delete: moves the note and all its active descendants to the recycle bin.
    /// References in other notes and uploaded files are left untouched so a
    /// restore is lossless; cleanup is deferred to permanent deletion.
    /// </summary>
    public async Task<bool> Delete(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return false;

        var deletedAt = NoteReferenceRewriter.UtcNowMicroseconds();
        var groupId = Guid.NewGuid();
        note.DeletedAt = deletedAt;
        note.IsDeletionRoot = true;
        note.DeletionGroupId = groupId;

        // Active descendants are swept into the same recycle bin group (shared group id).
        // Descendants soft-deleted earlier keep their own group and restore independently.
        var descendants = await CollectActiveDescendantsAsync(id, ct);
        foreach (var descendant in descendants)
        {
            descendant.DeletedAt = deletedAt;
            descendant.IsDeletionRoot = false;
            descendant.DeletionGroupId = groupId;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Note {NoteId} moved to recycle bin with {DescendantCount} descendant(s).",
            id, descendants.Count);
        return true;
    }

    public async Task<PagedResult<RecycleBinNoteSummary>> GetRecycleBin(int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Notes.IgnoreQueryFilters()
            .Where(n => n.DeletedAt != null && n.IsDeletionRoot);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(n => n.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new RecycleBinNoteSummary
            {
                Id = n.Id,
                Title = n.Title,
                DeletedAt = n.DeletedAt!.Value,
                IsArchived = n.ArchivedAt != null
            })
            .ToListAsync(ct);

        // Direct soft-deleted children per listed root (two-step, mirrors GetChildCountsAsync)
        var ids = items.Select(i => i.Id).ToList();
        if (ids.Count > 0)
        {
            var childCounts = await db.Notes.IgnoreQueryFilters()
                .Where(n => n.DeletedAt != null
                    && n.ParentNoteId.HasValue
                    && ids.Contains(n.ParentNoteId.Value))
                .GroupBy(n => n.ParentNoteId!.Value)
                .Select(g => new { ParentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);
            foreach (var item in items)
                item.ChildCount = childCounts.GetValueOrDefault(item.Id);
        }

        return new PagedResult<RecycleBinNoteSummary>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Restores a deletion root and the descendants that were soft-deleted together
    /// with it (same DeletedAt). If the original parent is missing or itself
    /// soft-deleted, the note is reattached as a root note.
    /// </summary>
    public async Task<bool> Restore(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(n =>
                n.Id == id && n.DeletedAt != null && n.IsDeletionRoot, ct);
        if (note is null) return false;

        var groupId = note.DeletionGroupId!.Value;
        var groupMembers = await CollectDeletedGroupDescendantsAsync(id, groupId, ct);

        if (note.ParentNoteId.HasValue)
        {
            // Filtered query: missing or soft-deleted parent → detach. An archived
            // parent also detaches, unless the restored root is itself archived —
            // then it returns to the archive where an archived parent is a legal home.
            var parentIsValid = note.ArchivedAt is not null
                ? await db.Notes.AnyAsync(n => n.Id == note.ParentNoteId.Value, ct)
                : await db.Notes.AnyAsync(n => n.Id == note.ParentNoteId.Value && n.ArchivedAt == null, ct);
            if (!parentIsValid)
                note.ParentNoteId = null;
        }

        note.DeletedAt = null;
        note.IsDeletionRoot = false;
        note.DeletionGroupId = null;
        foreach (var member in groupMembers)
        {
            member.DeletedAt = null;
            member.IsDeletionRoot = false;
            member.DeletionGroupId = null;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Note {NoteId} restored from recycle bin with {DescendantCount} descendant(s).",
            id, groupMembers.Count);
        return true;
    }

    /// <summary>
    /// Archives the note and all of its active descendants in one operation
    /// (shared ArchivedAt = restore group). Already-archived subtrees keep their
    /// own group and unarchive independently. Idempotent: archiving an archived
    /// note is a no-op. Returns false when the note is not found (or is in the
    /// recycle bin, which the global filter hides from this lookup).
    /// </summary>
    public async Task<bool> Archive(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return false;
        if (note.ArchivedAt is not null) return true; // idempotent no-op

        var archivedAt = NoteReferenceRewriter.UtcNowMicroseconds();
        var groupId = Guid.NewGuid();
        note.ArchivedAt = archivedAt;
        note.IsArchiveRoot = true;
        note.ArchiveGroupId = groupId;

        // Sweep active descendants into the same archive group (shared group id). The
        // BFS sees archived descendants too (archive has no global filter), so skip
        // them: independently archived subtrees keep their own root and group id.
        var descendants = (await CollectActiveDescendantsAsync(id, ct))
            .Where(d => d.ArchivedAt == null)
            .ToList();
        foreach (var descendant in descendants)
        {
            descendant.ArchivedAt = archivedAt;
            descendant.IsArchiveRoot = false;
            descendant.ArchiveGroupId = groupId;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Note {NoteId} archived with {DescendantCount} descendant(s).",
            id, descendants.Count);
        return true;
    }

    /// <summary>
    /// Unarchives the note and the descendants archived in the same operation
    /// (same ArchivedAt). Independently archived subtrees stay archived. If the
    /// original parent is missing, soft-deleted, or still archived, the note is
    /// reattached as a root note. Returns false when the note is not found or
    /// not archived.
    /// </summary>
    public async Task<bool> Unarchive(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n =>
            n.Id == id && n.ArchivedAt != null, ct);
        if (note is null) return false;

        var groupId = note.ArchiveGroupId!.Value;
        var groupMembers = await CollectArchivedGroupDescendantsAsync(id, groupId, ct);

        note.ArchivedAt = null;
        note.IsArchiveRoot = false;
        note.ArchiveGroupId = null;
        foreach (var member in groupMembers)
        {
            member.ArchivedAt = null;
            member.IsArchiveRoot = false;
            member.ArchiveGroupId = null;
        }

        // Never resurrect an active note under a missing, soft-deleted, or
        // archived parent (the filtered query hides the first two).
        if (note.ParentNoteId.HasValue)
        {
            var parentIsActive = await db.Notes.AnyAsync(n =>
                n.Id == note.ParentNoteId.Value && n.ArchivedAt == null, ct);
            if (!parentIsActive)
                note.ParentNoteId = null;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Note {NoteId} unarchived with {DescendantCount} descendant(s).",
            id, groupMembers.Count);
        return true;
    }

    /// <summary>
    /// Paged list of archive roots, newest first. The global filter automatically
    /// excludes archived notes that are currently in the recycle bin.
    /// </summary>
    public async Task<PagedResult<ArchivedNoteSummary>> GetArchive(int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Notes
            .Where(n => n.ArchivedAt != null && n.IsArchiveRoot);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(n => n.ArchivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new ArchivedNoteSummary
            {
                Id = n.Id,
                Title = n.Title,
                ArchivedAt = n.ArchivedAt!.Value
            })
            .ToListAsync(ct);

        // Direct children swept in the same archive operation (same group id),
        // mirroring the recycle bin's per-root child counts.
        var ids = items.Select(i => i.Id).ToList();
        if (ids.Count > 0)
        {
            var rootGroupIds = await db.Notes
                .Where(n => ids.Contains(n.Id))
                .Select(n => new { n.Id, n.ArchiveGroupId })
                .ToDictionaryAsync(x => x.Id, x => x.ArchiveGroupId, ct);
            var children = await db.Notes
                .Where(n => n.ArchivedAt != null
                    && n.ParentNoteId.HasValue
                    && ids.Contains(n.ParentNoteId.Value))
                .Select(n => new { ParentId = n.ParentNoteId!.Value, n.ArchiveGroupId })
                .ToListAsync(ct);
            foreach (var item in items)
                item.ChildCount = children.Count(c =>
                    c.ParentId == item.Id && c.ArchiveGroupId == rootGroupIds.GetValueOrDefault(item.Id));
        }

        return new PagedResult<ArchivedNoteSummary>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Permanently deletes a soft-deleted note. Returns (Found, WasInRecycleBin):
    /// active notes are refused so the recycle bin cannot be bypassed.
    /// </summary>
    public async Task<(bool Found, bool WasInRecycleBin)> DeletePermanent(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return (false, false);
        if (note.DeletedAt is null) return (true, false);

        await HardDeleteAsync(note, ct);
        return (true, true);
    }

    /// <summary>Permanently deletes every soft-deleted note of the user. Returns the root count.</summary>
    public async Task<int> EmptyRecycleBin(CancellationToken ct = default)
    {
        var rootIds = await db.Notes.IgnoreQueryFilters()
            .Where(n => n.DeletedAt != null && n.IsDeletionRoot)
            .Select(n => n.Id)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var rootId in rootIds)
        {
            // Re-fetch: an earlier iteration may have cascade-deleted this root
            // (a separately-soft-deleted sub-note of another soft-deleted parent).
            var note = await db.Notes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.Id == rootId && n.DeletedAt != null, ct);
            if (note is null) continue;
            await HardDeleteAsync(note, ct);
            purged++;
        }

        logger.LogInformation("Recycle Bin emptied: {Count} note(s) purged.", purged);
        return purged;
    }

    /// <summary>
    /// Permanently deletes deletion roots soft-deleted before <paramref name="cutoffUtc"/>.
    /// Called by <see cref="RecycleBinPurgeJob"/>. Returns the number of roots purged.
    /// </summary>
    public async Task<int> PurgeExpired(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var rootIds = await db.Notes.IgnoreQueryFilters()
            .Where(n => n.IsDeletionRoot && n.DeletedAt != null && n.DeletedAt < cutoffUtc)
            .Select(n => n.Id)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var rootId in rootIds)
        {
            ct.ThrowIfCancellationRequested();
            var note = await db.Notes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.Id == rootId && n.DeletedAt != null, ct);
            if (note is null) continue;
            await HardDeleteAsync(note, ct);
            purged++;
        }

        return purged;
    }

    /// <summary>
    /// Hard delete (previous Delete behavior): strips page-link/mention references
    /// from remaining notes, deletes the row (DB cascades the subtree), then
    /// cleans up files orphaned by the whole subtree's content.
    /// </summary>
    private async Task HardDeleteAsync(NoteItem note, CancellationToken ct = default)
    {
        var subtree = await CollectDescendantsUnfilteredAsync(note.Id, ct);

        var combinedContent = string.Join(' ',
            subtree.Select(n => n.Content).Prepend(note.Content));

        // Wrap the multi-save sequence (parent page-link strip, mention stripping,
        // and the note removal) in a single transaction so a mid-sequence failure
        // rolls the whole unit back instead of leaving a partial commit. The DB is
        // configured with a retrying execution strategy (EnableRetryOnFailure),
        // which forbids user-initiated transactions unless the whole unit runs
        // through the strategy so it can be retried atomically — mirrors
        // AccountService.PurgeAllDataAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Remove any page-link blocks referencing this note from the parent's content
            if (note.ParentNoteId.HasValue)
            {
                var parent = await db.Notes.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(n => n.Id == note.ParentNoteId.Value, ct);
                if (parent is not null)
                {
                    var cleaned = NoteReferenceRewriter.RemovePageLinkFromContent(parent.Content, note.Id);
                    if (cleaned != parent.Content)
                    {
                        parent.Content = cleaned;
                        parent.ContentText = NoteReferenceRewriter.StripHtml(cleaned);
                    }
                }
            }

            // Strip mention links referencing any note in the subtree from active notes
            await StripMentionReferencesAsync(note.Id, ct);
            foreach (var descendant in subtree)
                await StripMentionReferencesAsync(descendant.Id, ct);

            db.Notes.Remove(note);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

        // File cleanup runs AFTER the transaction commits: deleting files is an
        // irreversible filesystem side effect and must not sit inside the DB
        // transaction (a rollback cannot un-delete files).
        await fileCleanup.DeleteOrphanedFromContentAsync(combinedContent, ct);
        logger.LogInformation(
            "Note {NoteId} permanently deleted with {DescendantCount} descendant(s).",
            note.Id, subtree.Count);
    }

    /// <summary>All active descendants only (soft-deleted notes excluded, and descent stops at them).</summary>
    private async Task<List<NoteItem>> CollectActiveDescendantsAsync(Guid rootId, CancellationToken ct = default)
        => await CollectDescendantsAsync(rootId, """ AND n."DeletedAt" IS NULL""", [rootId], ct);

    /// <summary>All descendants regardless of recycle bin state.</summary>
    private async Task<List<NoteItem>> CollectDescendantsUnfilteredAsync(Guid rootId, CancellationToken ct = default)
        => await CollectDescendantsAsync(rootId, "", [rootId], ct);

    /// <summary>Soft-deleted descendants sharing the given recycle-bin group id.</summary>
    private async Task<List<NoteItem>> CollectDeletedGroupDescendantsAsync(Guid rootId, Guid groupId, CancellationToken ct = default)
        => await CollectDescendantsAsync(rootId, """ AND n."DeletionGroupId" = {1}""", [rootId, groupId], ct);

    /// <summary>Archived descendants sharing the given archive group id.
    /// Descends only through same-group (and non-deleted, matching the old global filter) notes,
    /// so independently archived subtrees stay put.</summary>
    private async Task<List<NoteItem>> CollectArchivedGroupDescendantsAsync(Guid rootId, Guid groupId, CancellationToken ct = default)
        => await CollectDescendantsAsync(rootId, """ AND n."ArchiveGroupId" = {1} AND n."DeletedAt" IS NULL""", [rootId, groupId], ct);

    /// <summary>
    /// Collects every descendant of <paramref name="rootId"/> in a single recursive CTE, replacing
    /// the former per-level BFS round trips (issue #305). <paramref name="nodeFilter"/> is an optional
    /// SQL predicate on the note alias <c>n</c> (e.g. <c>AND n."DeletedAt" IS NULL</c>) applied to both
    /// the anchor and every recursive step, exactly reproducing the per-level <c>Where</c> the BFS used —
    /// so a filtered-out note halts descent through it rather than merely being dropped from the result.
    /// Parameter <c>{0}</c> is always the root id; any extra parameters referenced as <c>{1}</c>, <c>{2}</c>…
    /// inside <paramref name="nodeFilter"/> follow, in order, in <paramref name="parameters"/>. The
    /// <c>Depth &lt; 100</c> guard preserves the old maxDepth cap against a cyclic parent chain. All
    /// filtering is explicit in the CTE, so global query filters are bypassed; entities are tracked so
    /// callers can mutate and save them exactly as before. Standard SQL: PostgreSQL (production) + SQLite (tests).
    /// </summary>
    private async Task<List<NoteItem>> CollectDescendantsAsync(
        Guid rootId, string nodeFilter, object[] parameters, CancellationToken ct = default)
    {
        var sql = $$"""
            WITH RECURSIVE "descendants" AS (
                SELECT n."Id", n."ParentNoteId", 1 AS "Depth"
                FROM "Notes" n
                WHERE n."ParentNoteId" = {0}{{nodeFilter}}
                UNION ALL
                SELECT n."Id", n."ParentNoteId", d."Depth" + 1
                FROM "Notes" n
                INNER JOIN "descendants" d ON n."ParentNoteId" = d."Id"
                WHERE d."Depth" < 100{{nodeFilter}}
            )
            SELECT * FROM "Notes" WHERE "Id" IN (SELECT "Id" FROM "descendants")
            """;

        return await db.Notes
            .FromSqlRaw(sql, parameters)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
    }

    private async Task StripMentionReferencesAsync(Guid noteId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters so recycle-bin (soft-deleted) notes referencing this
        // note also get their dead mention links stripped — otherwise a restored
        // note keeps a dead mention pointing at a permanently-deleted note.
        var referencingNotes = await NoteReferenceRewriter
            .WhereReferencesNote(db.Notes.IgnoreQueryFilters(), noteId, db.Database.IsNpgsql())
            .ToListAsync(ct);

        foreach (var n in referencingNotes)
        {
            var updated = NoteReferenceRewriter.StripMentionLinksFromContent(n.Content, noteId);
            if (updated == n.Content) continue;
            n.Content = updated;
            n.ContentText = NoteReferenceRewriter.StripHtml(updated);
        }

        if (referencingNotes.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
