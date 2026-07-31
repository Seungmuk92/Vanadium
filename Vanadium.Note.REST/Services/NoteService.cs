using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Core note service: CRUD, listing, full-text / mention / quick-nav search, backlinks, and
/// page-link/mention title propagation. Lifecycle transitions (archive / recycle bin / purge) and
/// anonymous sharing were split into <see cref="NoteLifecycleService"/> and
/// <see cref="NoteShareService"/> (issue #311); this class delegates its lifecycle and share methods
/// to them so the public API — and therefore the controller and test surface — is unchanged. Pure
/// content-reference rewriting lives in <see cref="NoteReferenceRewriter"/>.
/// </summary>
public class NoteService(
    NoteDbContext db,
    IHtmlSanitizerService htmlSanitizer,
    NoteLifecycleService lifecycle,
    NoteShareService share,
    ILogger<NoteService> logger)
{
    public async Task<PagedResult<NoteSummary>> GetPaged(
        int page,
        int pageSize,
        string? search,
        string sortBy,
        string sortDir,
        IReadOnlyList<PropertyFilter>? propertyFilters = null,
        CancellationToken ct = default)
    {
        // When not searching, show active root notes only.
        // When searching, archived notes are included (flagged IsArchived for the badge).
        bool rootOnly = string.IsNullOrWhiteSpace(search);

        IQueryable<NoteItem> allNotes = db.Notes;
        var baseNotes = rootOnly
            ? allNotes.Where(n => n.ParentNoteId == null && n.ArchivedAt == null)
            : allNotes;

        // Resolve + validate property filters and (non-search) property sort up front so a bad
        // pf/sortBy fails fast with a 400 before any DB read (PropertyQueryException).
        var resolvedFilters = await ResolveFiltersAsync(propertyFilters, ct);
        var sortDefinition = rootOnly ? await ResolveSortDefinitionAsync(sortBy, ct) : null;

        // Lean query for COUNT — no projection of per-note collections
        var countQuery = ApplyPropertyFilters(ApplyFilters(baseNotes, search), resolvedFilters);
        var totalCount = await countQuery.CountAsync(ct);

        // Full query for data — projects to NoteSummary to avoid fetching large Content column
        var baseDataQuery = ApplyPropertyFilters(ApplyFilters(baseNotes, search), resolvedFilters);

        IQueryable<NoteItem> orderedQuery;
        if (!string.IsNullOrWhiteSpace(search))
            orderedQuery = baseDataQuery.OrderByDescending(n => n.UpdatedAt);
        else if (sortDefinition is not null)
            orderedQuery = ApplyPropertySort(baseDataQuery, sortDefinition, sortDir);
        else
            orderedQuery = (sortBy.ToLowerInvariant(), sortDir.ToLowerInvariant()) switch
            {
                ("title", "asc")  => baseDataQuery.OrderBy(n => n.Title),
                ("title", "desc") => baseDataQuery.OrderByDescending(n => n.Title),
                ("date",  "asc")  => baseDataQuery.OrderBy(n => n.UpdatedAt),
                _                 => baseDataQuery.OrderByDescending(n => n.UpdatedAt)
            };

        var summaries = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.UpdatedAt,
                n.ParentNoteId,
                IsArchived = n.ArchivedAt != null,
                Properties = n.PropertyValues.Select(v => new PropertyValueProjection
                {
                    DefinitionId = v.DefinitionId,
                    Name = v.Definition.Name,
                    Type = v.Definition.Type,
                    SortOrder = v.Definition.SortOrder,
                    TextValue = v.TextValue,
                    NumberValue = v.NumberValue,
                    DateValue = v.DateValue,
                    BoolValue = v.BoolValue,
                    OptionId = v.SelectedOptionId,
                    OptionIds = v.SelectedOptions.Select(s => s.OptionId).ToList()
                }).ToList()
            })
            .ToListAsync(ct);

        var childCounts = await GetChildCountsAsync(summaries.Select(n => n.Id), ct);

        // Batch-fetch parent titles for sub-notes that surfaced via search
        Dictionary<Guid, string> parentTitles = [];
        if (!rootOnly)
        {
            var parentIds = summaries
                .Where(n => n.ParentNoteId.HasValue)
                .Select(n => n.ParentNoteId!.Value)
                .Distinct()
                .ToList();
            if (parentIds.Count > 0)
                parentTitles = await db.Notes
                    .Where(n => parentIds.Contains(n.Id))
                    .ToDictionaryAsync(n => n.Id, n => n.Title, ct);
        }

        logger.LogDebug("GetPaged: page={Page}, pageSize={PageSize}, total={Total}.", page, pageSize, totalCount);

        return new PagedResult<NoteSummary>
        {
            Items = summaries.Select(n => new NoteSummary
            {
                Id = n.Id,
                Title = n.Title,
                UpdatedAt = n.UpdatedAt,
                ParentNoteId = n.ParentNoteId,
                ParentTitle = n.ParentNoteId.HasValue ? parentTitles.GetValueOrDefault(n.ParentNoteId.Value) : null,
                ChildCount = childCounts.GetValueOrDefault(n.Id),
                IsArchived = n.IsArchived,
                Properties = ToValueDtos(n.Properties)
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<NoteSummary>> GetAllSummaries(
        IReadOnlyList<PropertyFilter>? propertyFilters = null,
        CancellationToken ct = default)
    {
        // Summaries never include archived notes.
        var query = db.Notes.Where(n => n.ArchivedAt == null);

        // AND semantics across pf entries.
        var resolvedFilters = await ResolveFiltersAsync(propertyFilters, ct);
        query = ApplyPropertyFilters(query, resolvedFilters);

        var summaries = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.UpdatedAt,
                n.ParentNoteId,
                Properties = n.PropertyValues.Select(v => new PropertyValueProjection
                {
                    DefinitionId = v.DefinitionId,
                    Name = v.Definition.Name,
                    Type = v.Definition.Type,
                    SortOrder = v.Definition.SortOrder,
                    TextValue = v.TextValue,
                    NumberValue = v.NumberValue,
                    DateValue = v.DateValue,
                    BoolValue = v.BoolValue,
                    OptionId = v.SelectedOptionId,
                    OptionIds = v.SelectedOptions.Select(s => s.OptionId).ToList()
                }).ToList()
            })
            .ToListAsync(ct);

        var childCounts = await GetChildCountsAsync(summaries.Select(n => n.Id), ct);

        logger.LogDebug("GetAllSummaries: {Count} note(s).", summaries.Count);
        return summaries.Select(n => new NoteSummary
        {
            Id = n.Id,
            Title = n.Title,
            UpdatedAt = n.UpdatedAt,
            ParentNoteId = n.ParentNoteId,
            ChildCount = childCounts.GetValueOrDefault(n.Id),
            Properties = ToValueDtos(n.Properties)
        }).ToList();
    }

    public async Task<List<NoteSummary>> GetChildren(Guid parentId, CancellationToken ct = default)
    {
        var summaries = await db.Notes
            .Where(n => n.ParentNoteId == parentId && n.ArchivedAt == null)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.UpdatedAt,
                n.ParentNoteId
            })
            .ToListAsync(ct);

        var childCounts = await GetChildCountsAsync(summaries.Select(n => n.Id), ct);

        logger.LogDebug("GetChildren: parentId={ParentId}, count={Count}.", parentId, summaries.Count);
        return summaries.Select(n => new NoteSummary
        {
            Id = n.Id,
            Title = n.Title,
            UpdatedAt = n.UpdatedAt,
            ParentNoteId = n.ParentNoteId,
            ChildCount = childCounts.GetValueOrDefault(n.Id)
        }).ToList();
    }

    public async Task<NoteItem?> Get(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes
            .Include(n => n.PropertyValues)
            .ThenInclude(v => v.Definition)
            .Include(n => n.PropertyValues)
            .ThenInclude(v => v.SelectedOptions)
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        if (note is null) return null;

        PopulateProperties(note);
        note.ChildCount = await db.Notes.CountAsync(n => n.ParentNoteId == id, ct);

        if (note.ParentNoteId.HasValue)
        {
            note.ParentTitle = await db.Notes
                .Where(n => n.Id == note.ParentNoteId.Value)
                .Select(n => n.Title)
                .FirstOrDefaultAsync(ct);
        }

        return note;
    }

    // ── Sharing (delegated to NoteShareService, issue #311) ──────────────────────

    /// <inheritdoc cref="NoteShareService.SetShare"/>
    public Task<ShareInfo?> SetShare(Guid id, ShareMode mode, CancellationToken ct = default)
        => share.SetShare(id, mode, ct);

    /// <inheritdoc cref="NoteShareService.Unshare"/>
    public Task<bool> Unshare(Guid id, CancellationToken ct = default)
        => share.Unshare(id, ct);

    /// <inheritdoc cref="NoteShareService.GetShareInfo"/>
    public Task<ShareInfo?> GetShareInfo(Guid id, CancellationToken ct = default)
        => share.GetShareInfo(id, ct);

    /// <inheritdoc cref="NoteShareService.GetSharedByToken"/>
    public Task<NoteItem?> GetSharedByToken(string? token, CancellationToken ct = default)
        => share.GetSharedByToken(token, ct);

    /// <inheritdoc cref="NoteShareService.ReSanitizeAllContentAsync"/>
    public Task<int> ReSanitizeAllContentAsync(CancellationToken ct = default)
        => share.ReSanitizeAllContentAsync(ct);

    public async Task<NoteItem> Create(NoteItem note, CancellationToken ct = default)
    {
        note.Id = Guid.NewGuid();
        note.UpdatedAt = NoteReferenceRewriter.UtcNowMicroseconds();
        // Sanitize before persisting so stored HTML can never carry active
        // content (script/event handlers), then derive the search text from the
        // sanitized markup.
        note.Content = htmlSanitizer.Sanitize(note.Content);
        note.ContentText = NoteReferenceRewriter.StripHtml(note.Content);
        // Server-owned lifecycle fields: force to the active state so a client
        // cannot over-post DeletedAt/ArchivedAt and create a note that is hidden
        // by the soft-delete filter (and silently purged) or born archived.
        note.DeletedAt = null;
        note.IsDeletionRoot = false;
        note.DeletionGroupId = null;
        note.ArchivedAt = null;
        note.IsArchiveRoot = false;
        note.ArchiveGroupId = null;
        // Sharing is off at birth and can only be turned on through the dedicated share
        // endpoints — a client must never be able to mint a share token via create/update.
        note.ShareToken = null;
        note.ShareMode = ShareMode.None;
        note.SharedAt = null;
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return note;
    }

    public async Task<(NoteItem? Note, bool Conflict, bool Archived)> Update(Guid id, NoteItem note, bool forceSave = false, CancellationToken ct = default)
    {
        var existing = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (existing is null) return (null, false, false);

        // Archived notes are read-only. Checked before the concurrency check so a
        // stale editor session gets a clear "archived" signal, not a conflict dialog.
        if (existing.ArchivedAt is not null)
        {
            logger.LogWarning("Update rejected — note {NoteId} is archived and read-only.", id);
            return (null, false, true);
        }

        var titleChanged = existing.Title != note.Title;

        // Capture the client's claimed version before mutating the tracked row:
        // callers may hand us the tracked entity itself as `note`, so reading
        // note.UpdatedAt after the stamp below would read the new value, not the
        // version the client actually knew.
        var clientVersion = note.UpdatedAt;

        existing.Title = note.Title;
        // Sanitize on the update path too — a leaked PAT could otherwise store a
        // payload via PUT just as easily as via POST.
        note.Content = htmlSanitizer.Sanitize(note.Content);
        existing.Content = note.Content;
        existing.ContentText = NoteReferenceRewriter.StripHtml(note.Content);
        existing.ParentNoteId = note.ParentNoteId;
        existing.UpdatedAt = NoteReferenceRewriter.UtcNowMicroseconds();

        // Optimistic concurrency: pin the client's claimed version as the
        // concurrency token's original value so EF enforces the check in the
        // UPDATE's WHERE clause at the DB level — the DB, not an in-memory
        // compare, decides the conflict, so a write racing between our read and
        // save can no longer be lost. This runs for EVERY non-force save,
        // including a default/zero version: a zero can never match a real row, so
        // it conflicts instead of silently overwriting (#221) — a client can no
        // longer bypass the check merely by omitting the version.
        //
        // Force-save is the only bypass and must be an explicit, server-authorized
        // action (the `force` flag, wired from a dedicated endpoint parameter):
        // leave the token at the freshly-read DB value so the save proceeds (only
        // a genuine mid-flight race can still conflict it).
        if (!forceSave)
            db.Entry(existing).Property(e => e.UpdatedAt).OriginalValue = clientVersion;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "Conflict on note {NoteId}: client version {ClientVersion} no longer matches the server row.",
                id, note.UpdatedAt);
            return (null, true, false);
        }

        if (titleChanged)
            await UpdatePageLinkReferences(id, note.Title, ct);

        return (existing, false, false);
    }

    private async Task UpdatePageLinkReferences(Guid noteId, string newTitle, CancellationToken ct = default)
    {
        // IgnoreQueryFilters so recycle-bin (soft-deleted) notes referencing this
        // note also get their page-link title refreshed — otherwise a restored note
        // keeps a stale title.
        var referencingNotes = await NoteReferenceRewriter
            .WhereReferencesNote(db.Notes.IgnoreQueryFilters(), noteId, db.Database.IsNpgsql())
            .ToListAsync(ct);

        if (referencingNotes.Count == 0) return;

        // Save each referencing note on its own so one note losing an optimistic-concurrency race
        // cannot abort propagation to the others, and so the conflict can be resolved per note
        // (issue #298). The previous single batch save neither advanced UpdatedAt nor tolerated a
        // conflict — a concurrent edit to any referencing note would surface as an uncaught
        // DbUpdateConcurrencyException that failed the whole request after the title note had
        // already been saved.
        var propagated = 0;
        foreach (var n in referencingNotes)
        {
            if (await TryPropagateTitleToNote(n, noteId, newTitle, ct))
                propagated++;
        }

        logger.LogInformation(
            "Propagated title change to '{NewTitle}' across {Count} of {Total} note(s) referencing {NoteId}.",
            newTitle, propagated, referencingNotes.Count, noteId);
    }

    /// <summary>
    /// Rewrites the page-link/mention titles that reference <paramref name="referencedId"/> inside a
    /// single note, advances its microsecond <see cref="NoteItem.UpdatedAt"/>, and saves it on its
    /// own. Because <c>UpdatedAt</c> is a <c>[ConcurrencyCheck]</c> token, EF pins the version we
    /// read into the UPDATE's WHERE clause; if a concurrent editor advanced the row in between, the
    /// save conflicts and we reload the note's current content and re-apply the title on top of that
    /// edit instead of silently overwriting it. Returns true when the note was persisted with the new
    /// title. Best-effort: after a bounded number of losing races the note is left untouched (its own
    /// next save will refresh the stale title), so a hot-edited note never blocks the request.
    /// </summary>
    private async Task<bool> TryPropagateTitleToNote(
        NoteItem note, Guid referencedId, string newTitle, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var updated = NoteReferenceRewriter.UpdatePageLinkTitleInContent(note.Content, referencedId, newTitle);
            updated = NoteReferenceRewriter.UpdateMentionTitleInContent(updated, referencedId, newTitle);
            if (updated == note.Content) return false;

            note.Content = updated;
            note.ContentText = NoteReferenceRewriter.StripHtml(updated);
            // Advance the version so the change is detectable by optimistic concurrency; the
            // pre-save (original) value remains the token EF places in the UPDATE's WHERE clause.
            note.UpdatedAt = NoteReferenceRewriter.UtcNowMicroseconds();

            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent edit advanced this note between our read and save. Reload its current
                // state and retry so the title change layers onto that edit rather than clobbering it.
                await db.Entry(note).ReloadAsync(ct);
                if (db.Entry(note).State == EntityState.Detached)
                {
                    // The referencing note was hard-deleted mid-propagation — nothing left to update.
                    return false;
                }
            }
        }

        logger.LogWarning(
            "Skipped title propagation to note {NoteId} after {Attempts} conflicting attempts; its "
            + "next own save will refresh the stale reference title.",
            note.Id, maxAttempts);
        return false;
    }

    /// <summary>
    /// Returns true if making <paramref name="proposedParentId"/> the parent of
    /// <paramref name="noteId"/> would create a cycle in the ancestor chain.
    /// </summary>
    public async Task<bool> HasCircularReference(Guid noteId, Guid proposedParentId, CancellationToken ct = default)
    {
        // A note can never be reparented under itself. This also mirrors the old BFS, which
        // evaluated this equality before issuing any (filtered) DB lookup for the parent.
        if (proposedParentId == noteId) return true;

        // Walk the ancestor chain upward from the proposed parent in a single recursive CTE
        // instead of one DB round trip per level (issue #305). A cycle exists when noteId turns
        // up anywhere above the proposed parent. Traversal follows only non-deleted notes, exactly
        // mirroring the former db.Notes (global-filter) BFS which stopped at a soft-deleted ancestor.
        // The Depth < 100 guard preserves the old maxDepth cap so a pre-existing cyclic parent chain
        // can never loop forever. Standard SQL that runs on PostgreSQL (production) and SQLite (tests).
        const string sql = """
            WITH RECURSIVE "ancestors" AS (
                SELECT n."Id", n."ParentNoteId", 1 AS "Depth"
                FROM "Notes" n
                WHERE n."Id" = {0} AND n."DeletedAt" IS NULL
                UNION ALL
                SELECT n."Id", n."ParentNoteId", a."Depth" + 1
                FROM "Notes" n
                INNER JOIN "ancestors" a ON n."Id" = a."ParentNoteId"
                WHERE a."Depth" < 100 AND n."DeletedAt" IS NULL
            )
            SELECT 1 AS "Value" FROM "ancestors" a WHERE a."Id" = {1} LIMIT 1
            """;

        var matches = await db.Database
            .SqlQueryRaw<int>(sql, proposedParentId, noteId)
            .ToListAsync(ct);
        return matches.Count > 0;
    }

    public async Task<List<MentionSuggestionDto>> SearchForMention(string query, int limit = 10, CancellationToken ct = default)
    {
        // Mentions target active work — archived notes are excluded.
        var q = db.Notes.Where(n => n.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{EscapeLikePattern(query.Trim())}%";
            q = q.Where(n => EF.Functions.ILike(n.Title, pattern));
        }
        return await q
            .OrderByDescending(n => n.UpdatedAt)
            .Take(limit)
            .Select(n => new MentionSuggestionDto { Id = n.Id, Title = n.Title })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Trigram-backed search for the Quick Navigation palette. Returns a lean projection
    /// (id, title, snippet, archived flag) for the current user, ordered by recency.
    /// Archived notes are INCLUDED (no <c>ArchivedAt == null</c> predicate); the default
    /// <c>DeletedAt == null</c> global filter excludes Recycle Bin notes — no opt-out needed.
    /// </summary>
    public async Task<List<QuickNavResult>> QuickSearch(string query, int limit = 20, CancellationToken ct = default)
    {
        var terms = (query ?? string.Empty).Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return [];

        limit = Math.Clamp(limit, 1, 50);

        // Global filter hides Recycle Bin notes. No n.ArchivedAt == null → archived INCLUDED (FR-4).
        IQueryable<NoteItem> q = db.Notes;
        foreach (var term in terms)
        {
            var pattern = $"%{EscapeLikePattern(term)}%";
            q = q.Where(n =>
                EF.Functions.ILike(n.Title, pattern) ||
                EF.Functions.ILike(n.ContentText, pattern));
        }

        var rows = await q
            .OrderByDescending(n => n.UpdatedAt)
            .Take(limit)
            .Select(n => new { n.Id, n.Title, n.ContentText, ArchivedAt = n.ArchivedAt })
            .ToListAsync(ct);

        return rows.Select(r => new QuickNavResult
        {
            Id = r.Id,
            Title = r.Title,
            Snippet = BuildSnippet(r.ContentText, terms),
            IsArchived = r.ArchivedAt != null
        }).ToList();
    }

    /// <summary>
    /// Builds a short plain-text preview around the first matching term. Runs in memory on
    /// the capped result set, never touches the DB. <c>ContentText</c> is already tag-stripped,
    /// so the snippet is plain text with no markup-injection risk.
    /// </summary>
    internal static string BuildSnippet(string? contentText, string[] terms)
    {
        if (string.IsNullOrEmpty(contentText)) return string.Empty;

        const int windowBefore = 30;
        const int maxLength = 160;

        var idx = -1;
        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term)) continue;
            var found = contentText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (found >= 0 && (idx < 0 || found < idx))
                idx = found;
        }

        // Title-only match (no term in content): fall back to the leading slice.
        var start = idx < 0 ? 0 : Math.Max(0, idx - windowBefore);
        var length = Math.Min(maxLength, contentText.Length - start);
        var slice = contentText.Substring(start, length);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = start + length < contentText.Length ? "…" : string.Empty;
        return prefix + slice + suffix;
    }

    /// <summary>
    /// "What links here": returns notes whose HTML content references the given note via a
    /// <c>data-note-id="{id}"</c> attribute (mentions and page links). Reuses the same indexed
    /// reference probe (<see cref="NoteReferenceRewriter.WhereReferencesNote"/>) as title propagation
    /// (<see cref="UpdatePageLinkReferences"/>); a normalized reference table is intentionally out
    /// of scope (issue #141).
    /// Soft-deleted notes are excluded by the default global query filter (no
    /// <c>IgnoreQueryFilters()</c>); archived notes are INCLUDED and flagged via
    /// <see cref="BacklinkResult.IsArchived"/>, mirroring full-text/Quick Navigation search so
    /// the full reference graph is visible. The note itself is excluded from its own backlinks.
    /// </summary>
    public async Task<List<BacklinkResult>> GetBacklinks(Guid id, int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var rows = await NoteReferenceRewriter.WhereReferencesNote(db.Notes, id, db.Database.IsNpgsql())
            .Where(n => n.Id != id)
            .OrderByDescending(n => n.UpdatedAt)
            .Take(limit)
            .Select(n => new { n.Id, n.Title, n.ContentText, n.ArchivedAt })
            .ToListAsync(ct);

        return rows.Select(r => new BacklinkResult
        {
            Id = r.Id,
            Title = r.Title,
            // No search term for backlinks — BuildSnippet falls back to the leading slice.
            Snippet = BuildSnippet(r.ContentText, []),
            IsArchived = r.ArchivedAt != null
        }).ToList();
    }

    // ── Lifecycle (delegated to NoteLifecycleService, issue #311) ────────────────

    /// <inheritdoc cref="NoteLifecycleService.Delete"/>
    public Task<bool> Delete(Guid id, CancellationToken ct = default)
        => lifecycle.Delete(id, ct);

    /// <inheritdoc cref="NoteLifecycleService.GetRecycleBin"/>
    public Task<PagedResult<RecycleBinNoteSummary>> GetRecycleBin(int page, int pageSize, CancellationToken ct = default)
        => lifecycle.GetRecycleBin(page, pageSize, ct);

    /// <inheritdoc cref="NoteLifecycleService.Restore"/>
    public Task<bool> Restore(Guid id, CancellationToken ct = default)
        => lifecycle.Restore(id, ct);

    /// <inheritdoc cref="NoteLifecycleService.Archive"/>
    public Task<bool> Archive(Guid id, CancellationToken ct = default)
        => lifecycle.Archive(id, ct);

    /// <inheritdoc cref="NoteLifecycleService.Unarchive"/>
    public Task<bool> Unarchive(Guid id, CancellationToken ct = default)
        => lifecycle.Unarchive(id, ct);

    /// <inheritdoc cref="NoteLifecycleService.GetArchive"/>
    public Task<PagedResult<ArchivedNoteSummary>> GetArchive(int page, int pageSize, CancellationToken ct = default)
        => lifecycle.GetArchive(page, pageSize, ct);

    /// <inheritdoc cref="NoteLifecycleService.DeletePermanent"/>
    public Task<(bool Found, bool WasInRecycleBin)> DeletePermanent(Guid id, CancellationToken ct = default)
        => lifecycle.DeletePermanent(id, ct);

    /// <inheritdoc cref="NoteLifecycleService.EmptyRecycleBin"/>
    public Task<int> EmptyRecycleBin(CancellationToken ct = default)
        => lifecycle.EmptyRecycleBin(ct);

    /// <inheritdoc cref="NoteLifecycleService.PurgeExpired"/>
    public Task<int> PurgeExpired(DateTime cutoffUtc, CancellationToken ct = default)
        => lifecycle.PurgeExpired(cutoffUtc, ct);

    private static IQueryable<NoteItem> ApplyFilters(
        IQueryable<NoteItem> query, string? search)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var terms = search.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var term in terms)
            {
                var pattern = $"%{EscapeLikePattern(term)}%";
                query = query.Where(n =>
                    EF.Functions.ILike(n.Title, pattern) ||
                    EF.Functions.ILike(n.ContentText, pattern));
            }
        }

        return query;
    }

    private static string EscapeLikePattern(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private async Task<Dictionary<Guid, int>> GetChildCountsAsync(IEnumerable<Guid> noteIds, CancellationToken ct = default)
    {
        var ids = noteIds.ToList();
        if (ids.Count == 0) return [];
        return await db.Notes
            .Where(n => n.ParentNoteId.HasValue && ids.Contains(n.ParentNoteId!.Value))
            .GroupBy(n => n.ParentNoteId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);
    }

    // ── Note Properties (issue #343) ─────────────────────────────────────────────

    /// <summary>Thrown when a <c>pf</c> filter or a <c>sortBy=prop:</c> value is malformed, references
    /// an unknown definition, or mismatches its type. Mapped to 400 by the controller.</summary>
    public class PropertyQueryException(string message) : Exception(message);

    /// <summary>In-memory shape projected out of EF so property DTOs can be ordered by definition
    /// SortOrder without relying on ORDER BY translation inside a collection subquery.</summary>
    private sealed class PropertyValueProjection
    {
        public Guid DefinitionId { get; init; }
        public string Name { get; init; } = string.Empty;
        public PropertyType Type { get; init; }
        public int SortOrder { get; init; }
        public string? TextValue { get; init; }
        public double? NumberValue { get; init; }
        public DateOnly? DateValue { get; init; }
        public bool? BoolValue { get; init; }
        public Guid? OptionId { get; init; }
        public List<Guid> OptionIds { get; init; } = [];
    }

    private static List<NotePropertyValueDto> ToValueDtos(IEnumerable<PropertyValueProjection> values) =>
        values
            .OrderBy(v => v.SortOrder)
            .Select(v => new NotePropertyValueDto
            {
                DefinitionId = v.DefinitionId,
                Name = v.Name,
                Type = v.Type,
                TextValue = v.TextValue,
                NumberValue = v.NumberValue,
                DateValue = v.DateValue,
                BoolValue = v.BoolValue,
                OptionId = v.OptionId,
                OptionIds = v.OptionIds
            })
            .ToList();

    private static void PopulateProperties(NoteItem note) =>
        note.Properties = note.PropertyValues
            .OrderBy(v => v.Definition.SortOrder)
            .Select(v => new NotePropertyValueDto
            {
                DefinitionId = v.DefinitionId,
                Name = v.Definition.Name,
                Type = v.Definition.Type,
                TextValue = v.TextValue,
                NumberValue = v.NumberValue,
                DateValue = v.DateValue,
                BoolValue = v.BoolValue,
                OptionId = v.SelectedOptionId,
                OptionIds = v.SelectedOptions.Select(s => s.OptionId).ToList()
            })
            .ToList();

    // ── Property filtering (§7.3) ────────────────────────────────────────────────

    /// <summary>Resolved + validated filter carrying the parsed typed value(s).</summary>
    private sealed class ResolvedFilter
    {
        public required PropertyDefinition Definition { get; init; }
        public required PropertyFilterOp Op { get; init; }
        public string? Text { get; init; }
        public double Number { get; init; }
        public DateOnly Date { get; init; }
        public bool Bool { get; init; }
        public Guid Option { get; init; }
        public List<Guid> Options { get; init; } = [];
    }

    private async Task<List<ResolvedFilter>> ResolveFiltersAsync(
        IReadOnlyList<PropertyFilter>? filters, CancellationToken ct)
    {
        if (filters is null || filters.Count == 0) return [];
        if (filters.Count > 20)
            throw new PropertyQueryException("Too many property filters (maximum 20).");

        var ids = filters.Select(f => f.DefinitionId).Distinct().ToList();
        var definitions = await db.PropertyDefinitions
            .Include(d => d.Options)
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);

        var resolved = new List<ResolvedFilter>(filters.Count);
        foreach (var f in filters)
        {
            if (!definitions.TryGetValue(f.DefinitionId, out var def))
                throw new PropertyQueryException($"Unknown property definition '{f.DefinitionId}'.");
            resolved.Add(ResolveFilter(def, f));
        }
        return resolved;
    }

    private static ResolvedFilter ResolveFilter(PropertyDefinition def, PropertyFilter f)
    {
        // empty / notempty are valid for every type except Checkbox (two-state by design).
        if (f.Op is PropertyFilterOp.Empty or PropertyFilterOp.NotEmpty)
        {
            if (def.Type == PropertyType.Checkbox)
                throw new PropertyQueryException("empty/notempty is not valid for a Checkbox property.");
            return new ResolvedFilter { Definition = def, Op = f.Op };
        }

        string RawValue()
        {
            if (string.IsNullOrEmpty(f.RawValue))
                throw new PropertyQueryException($"Operator '{f.Op}' requires a value.");
            return f.RawValue;
        }

        List<Guid> ParseOptionList()
        {
            var ids = RawValue().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsed = new List<Guid>(ids.Length);
            foreach (var s in ids)
            {
                if (!Guid.TryParse(s, out var g))
                    throw new PropertyQueryException($"'{s}' is not a valid option id.");
                parsed.Add(g);
            }
            if (parsed.Count == 0)
                throw new PropertyQueryException("anyof requires at least one option id.");
            EnsureOptionsBelong(def, parsed);
            return parsed;
        }

        Guid ParseOption()
        {
            if (!Guid.TryParse(RawValue(), out var g))
                throw new PropertyQueryException($"'{f.RawValue}' is not a valid option id.");
            EnsureOptionsBelong(def, [g]);
            return g;
        }

        switch (def.Type)
        {
            case PropertyType.Text:
                RequireOp(def, f.Op, PropertyFilterOp.Eq, PropertyFilterOp.Ne);
                var text = RawValue();
                if (text.Length > PropertyService.MaxTextValueLength)
                    throw new PropertyQueryException($"Text filter value exceeds {PropertyService.MaxTextValueLength} characters.");
                return new ResolvedFilter { Definition = def, Op = f.Op, Text = text };

            case PropertyType.Number:
                RequireOp(def, f.Op, PropertyFilterOp.Eq, PropertyFilterOp.Ne, PropertyFilterOp.Lt,
                    PropertyFilterOp.Lte, PropertyFilterOp.Gt, PropertyFilterOp.Gte);
                if (!double.TryParse(RawValue(), NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                    || !double.IsFinite(num))
                    throw new PropertyQueryException($"'{f.RawValue}' is not a valid number.");
                return new ResolvedFilter { Definition = def, Op = f.Op, Number = num };

            case PropertyType.Date:
                RequireOp(def, f.Op, PropertyFilterOp.Eq, PropertyFilterOp.Ne, PropertyFilterOp.Lt,
                    PropertyFilterOp.Lte, PropertyFilterOp.Gt, PropertyFilterOp.Gte);
                if (!DateOnly.TryParseExact(RawValue(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                    throw new PropertyQueryException($"'{f.RawValue}' is not a valid date (expected yyyy-MM-dd).");
                return new ResolvedFilter { Definition = def, Op = f.Op, Date = date };

            case PropertyType.Checkbox:
                RequireOp(def, f.Op, PropertyFilterOp.Eq);
                var raw = RawValue();
                var b = raw switch
                {
                    "true" => true,
                    "false" => false,
                    _ => throw new PropertyQueryException("Checkbox filter value must be 'true' or 'false'.")
                };
                return new ResolvedFilter { Definition = def, Op = f.Op, Bool = b };

            case PropertyType.Select:
                if (f.Op == PropertyFilterOp.AnyOf)
                    return new ResolvedFilter { Definition = def, Op = f.Op, Options = ParseOptionList() };
                RequireOp(def, f.Op, PropertyFilterOp.Eq, PropertyFilterOp.Ne, PropertyFilterOp.AnyOf);
                return new ResolvedFilter { Definition = def, Op = f.Op, Option = ParseOption() };

            case PropertyType.MultiSelect:
                if (f.Op == PropertyFilterOp.AnyOf)
                    return new ResolvedFilter { Definition = def, Op = f.Op, Options = ParseOptionList() };
                RequireOp(def, f.Op, PropertyFilterOp.Eq, PropertyFilterOp.AnyOf);
                return new ResolvedFilter { Definition = def, Op = f.Op, Option = ParseOption() };

            default:
                throw new PropertyQueryException($"Unknown property type '{(int)def.Type}'.");
        }
    }

    private static void RequireOp(PropertyDefinition def, PropertyFilterOp op, params PropertyFilterOp[] allowed)
    {
        if (!allowed.Contains(op))
            throw new PropertyQueryException($"Operator '{op}' is not valid for a {def.Type} property.");
    }

    private static void EnsureOptionsBelong(PropertyDefinition def, IReadOnlyCollection<Guid> optionIds)
    {
        var valid = def.Options.Select(o => o.Id).ToHashSet();
        if (!optionIds.All(valid.Contains))
            throw new PropertyQueryException("One or more options do not belong to this property.");
    }

    private static IQueryable<NoteItem> ApplyPropertyFilters(
        IQueryable<NoteItem> query, IReadOnlyList<ResolvedFilter> filters)
    {
        foreach (var f in filters)
        {
            var d = f.Definition.Id;
            query = (f.Definition.Type, f.Op) switch
            {
                (_, PropertyFilterOp.Empty) =>
                    query.Where(n => !n.PropertyValues.Any(v => v.DefinitionId == d)),
                (_, PropertyFilterOp.NotEmpty) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d)),

                (PropertyType.Text, PropertyFilterOp.Eq) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.TextValue == f.Text)),
                (PropertyType.Text, PropertyFilterOp.Ne) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.TextValue != f.Text)),

                (PropertyType.Number, PropertyFilterOp.Eq) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.NumberValue == f.Number)),
                (PropertyType.Number, PropertyFilterOp.Ne) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.NumberValue != f.Number)),
                (PropertyType.Number, PropertyFilterOp.Lt) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.NumberValue < f.Number)),
                (PropertyType.Number, PropertyFilterOp.Lte) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.NumberValue <= f.Number)),
                (PropertyType.Number, PropertyFilterOp.Gt) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.NumberValue > f.Number)),
                (PropertyType.Number, PropertyFilterOp.Gte) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.NumberValue >= f.Number)),

                (PropertyType.Date, PropertyFilterOp.Eq) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.DateValue == f.Date)),
                (PropertyType.Date, PropertyFilterOp.Ne) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.DateValue != f.Date)),
                (PropertyType.Date, PropertyFilterOp.Lt) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.DateValue < f.Date)),
                (PropertyType.Date, PropertyFilterOp.Lte) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.DateValue <= f.Date)),
                (PropertyType.Date, PropertyFilterOp.Gt) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.DateValue > f.Date)),
                (PropertyType.Date, PropertyFilterOp.Gte) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.DateValue >= f.Date)),

                // Checkbox eq:true → a true row exists; eq:false → no true row (missing = unchecked).
                (PropertyType.Checkbox, PropertyFilterOp.Eq) when f.Bool =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.BoolValue == true)),
                (PropertyType.Checkbox, PropertyFilterOp.Eq) =>
                    query.Where(n => !n.PropertyValues.Any(v => v.DefinitionId == d && v.BoolValue == true)),

                (PropertyType.Select, PropertyFilterOp.Eq) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.SelectedOptionId == f.Option)),
                (PropertyType.Select, PropertyFilterOp.Ne) =>
                    query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == d && v.SelectedOptionId != f.Option)),
                (PropertyType.Select, PropertyFilterOp.AnyOf) =>
                    query.Where(n => n.PropertyValues.Any(v =>
                        v.DefinitionId == d && v.SelectedOptionId != null && f.Options.Contains(v.SelectedOptionId.Value))),

                (PropertyType.MultiSelect, PropertyFilterOp.Eq) =>
                    query.Where(n => n.PropertyValues.Any(v =>
                        v.DefinitionId == d && v.SelectedOptions.Any(s => s.OptionId == f.Option))),
                (PropertyType.MultiSelect, PropertyFilterOp.AnyOf) =>
                    query.Where(n => n.PropertyValues.Any(v =>
                        v.DefinitionId == d && v.SelectedOptions.Any(s => f.Options.Contains(s.OptionId)))),

                _ => throw new PropertyQueryException(
                    $"Operator '{f.Op}' is not valid for a {f.Definition.Type} property.")
            };
        }
        return query;
    }

    // ── Property sorting (§7.5) ──────────────────────────────────────────────────

    private async Task<PropertyDefinition?> ResolveSortDefinitionAsync(string sortBy, CancellationToken ct)
    {
        const string prefix = "prop:";
        if (string.IsNullOrEmpty(sortBy) || !sortBy.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var idText = sortBy[prefix.Length..];
        if (!Guid.TryParse(idText, out var id))
            throw new PropertyQueryException($"'{idText}' is not a valid property definition id.");

        var def = await db.PropertyDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
            throw new PropertyQueryException($"Unknown property definition '{id}'.");
        if (def.Type == PropertyType.MultiSelect)
            throw new PropertyQueryException("A MultiSelect property cannot be used for sorting.");
        return def;
    }

    /// <summary>Orders notes by a property value with empties always last (independent of direction),
    /// then by <c>UpdatedAt</c> desc as a stable tiebreak. A missing value row means empty (INV-P1),
    /// so the presence key is simply "has a row for this definition".</summary>
    private static IQueryable<NoteItem> ApplyPropertySort(
        IQueryable<NoteItem> query, PropertyDefinition def, string sortDir)
    {
        var d = def.Id;
        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var withPresence = query.OrderBy(n => n.PropertyValues.Any(v => v.DefinitionId == d) ? 0 : 1);

        IOrderedQueryable<NoteItem> ordered = def.Type switch
        {
            PropertyType.Text => asc
                ? withPresence.ThenBy(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.TextValue).FirstOrDefault())
                : withPresence.ThenByDescending(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.TextValue).FirstOrDefault()),
            PropertyType.Number => asc
                ? withPresence.ThenBy(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.NumberValue).FirstOrDefault())
                : withPresence.ThenByDescending(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.NumberValue).FirstOrDefault()),
            PropertyType.Date => asc
                ? withPresence.ThenBy(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.DateValue).FirstOrDefault())
                : withPresence.ThenByDescending(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.DateValue).FirstOrDefault()),
            PropertyType.Checkbox => asc
                ? withPresence.ThenBy(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.BoolValue).FirstOrDefault())
                : withPresence.ThenByDescending(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => v.BoolValue).FirstOrDefault()),
            PropertyType.Select => asc
                ? withPresence.ThenBy(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => (int?)v.SelectedOption!.SortOrder).FirstOrDefault())
                : withPresence.ThenByDescending(n => n.PropertyValues.Where(v => v.DefinitionId == d).Select(v => (int?)v.SelectedOption!.SortOrder).FirstOrDefault()),
            _ => withPresence
        };

        return ordered.ThenByDescending(n => n.UpdatedAt);
    }
}
