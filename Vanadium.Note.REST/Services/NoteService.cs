using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class NoteService(
    NoteDbContext db,
    FileCleanupService fileCleanup,
    IHtmlSanitizerService htmlSanitizer,
    ILogger<NoteService> logger)
{
    public async Task<PagedResult<NoteSummary>> GetPaged(
        int page,
        int pageSize,
        string? search,
        string sortBy,
        string sortDir,
        Guid[]? labelIds,
        CancellationToken ct = default)
    {
        // When not searching, show active root notes only.
        // When searching, archived notes are included (flagged IsArchived for the badge).
        bool rootOnly = string.IsNullOrWhiteSpace(search);

        IQueryable<NoteItem> allNotes = db.Notes;
        var baseNotes = rootOnly
            ? allNotes.Where(n => n.ParentNoteId == null && n.ArchivedAt == null)
            : allNotes;

        // Lean query for COUNT — no joins to label/category tables
        var countQuery = ApplyFilters(baseNotes, search, labelIds);
        var totalCount = await countQuery.CountAsync(ct);

        // Full query for data — projects to NoteSummary to avoid fetching large Content column
        var baseDataQuery = ApplyFilters(baseNotes, search, labelIds);

        var orderedQuery = !string.IsNullOrWhiteSpace(search)
            ? baseDataQuery.OrderByDescending(n => n.UpdatedAt)
            : (sortBy.ToLowerInvariant(), sortDir.ToLowerInvariant()) switch
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
                Labels = n.NoteLabels.Select(nl => new LabelSummary
                {
                    Id = nl.Label.Id,
                    Name = nl.Label.Name,
                    CategoryId = nl.Label.CategoryId,
                    CategoryName = nl.Label.Category == null ? null : nl.Label.Category.Name
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
                Labels = OrderLabelsForDisplay(n.Labels).ToList()
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<NoteSummary>> GetAllSummaries(Guid[]? labelIds = null, CancellationToken ct = default)
    {
        // The board never shows archived notes.
        var query = db.Notes.Where(n => n.ArchivedAt == null);

        // OR logic: notes that have ANY of the specified labels
        if (labelIds is { Length: > 0 })
            query = query.Where(n => n.NoteLabels.Any(nl => labelIds.Contains(nl.LabelId)));

        var summaries = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.UpdatedAt,
                n.ParentNoteId,
                Labels = n.NoteLabels.Select(nl => new LabelSummary
                {
                    Id = nl.Label.Id,
                    Name = nl.Label.Name,
                    CategoryId = nl.Label.CategoryId,
                    CategoryName = nl.Label.Category == null ? null : nl.Label.Category.Name
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
            Labels = OrderLabelsForDisplay(n.Labels).ToList()
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
                n.ParentNoteId,
                Labels = n.NoteLabels.Select(nl => new LabelSummary
                {
                    Id = nl.Label.Id,
                    Name = nl.Label.Name,
                    CategoryId = nl.Label.CategoryId,
                    CategoryName = nl.Label.Category == null ? null : nl.Label.Category.Name
                }).ToList()
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
            ChildCount = childCounts.GetValueOrDefault(n.Id),
            Labels = OrderLabelsForDisplay(n.Labels).ToList()
        }).ToList();
    }

    public async Task<NoteItem?> Get(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        if (note is null) return null;

        PopulateLabels(note);
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

    public async Task<NoteItem> Create(NoteItem note, CancellationToken ct = default)
    {
        note.Id = Guid.NewGuid();
        note.UpdatedAt = UtcNowMicroseconds();
        // Sanitize before persisting so stored HTML can never carry active
        // content (script/event handlers), then derive the search text from the
        // sanitized markup.
        note.Content = htmlSanitizer.Sanitize(note.Content);
        note.ContentText = StripHtml(note.Content);
        // Server-owned lifecycle fields: force to the active state so a client
        // cannot over-post DeletedAt/ArchivedAt and create a note that is hidden
        // by the soft-delete filter (and silently purged) or born archived.
        note.DeletedAt = null;
        note.IsDeletionRoot = false;
        note.ArchivedAt = null;
        note.IsArchiveRoot = false;
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return note;
    }

    public async Task<(NoteItem? Note, bool Conflict, bool Archived)> Update(Guid id, NoteItem note, CancellationToken ct = default)
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
        existing.ContentText = StripHtml(note.Content);
        existing.ParentNoteId = note.ParentNoteId;
        existing.UpdatedAt = UtcNowMicroseconds();

        // Optimistic concurrency: when the client sent the version it last saw,
        // pin it as the concurrency token's original value so EF enforces the
        // check in the UPDATE's WHERE clause at the DB level — the DB, not an
        // in-memory compare, decides the conflict, so a write racing between our
        // read and save can no longer be lost. A default version is the
        // force-save bypass: leave the token at the freshly-read DB value so the
        // save proceeds (only a genuine mid-flight race can still conflict it).
        if (clientVersion != default)
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
        var idStr = noteId.ToString();
        // IgnoreQueryFilters so recycle-bin (soft-deleted) notes referencing this
        // note also get their page-link title refreshed — otherwise a restored note
        // keeps a stale title.
        var referencingNotes = await db.Notes.IgnoreQueryFilters()
            .Where(n => n.Content.Contains($"data-note-id=\"{idStr}\""))
            .ToListAsync(ct);

        if (referencingNotes.Count == 0) return;

        foreach (var n in referencingNotes)
        {
            var updated = UpdatePageLinkTitleInContent(n.Content, noteId, newTitle);
            updated = UpdateMentionTitleInContent(updated, noteId, newTitle);
            if (updated == n.Content) continue;
            n.Content = updated;
            n.ContentText = StripHtml(updated);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Propagated title change to '{NewTitle}' across {Count} note(s) referencing {NoteId}.",
            newTitle, referencingNotes.Count, noteId);
    }

    private static string UpdateMentionTitleInContent(string content, Guid noteId, string newTitle)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var idStr = noteId.ToString();
        var encodedTitle = WebUtility.HtmlEncode(newTitle);

        return Regex.Replace(
            content,
            $@"(<a\s[^>]*data-note-id=""{Regex.Escape(idStr)}""[^>]*>)@[^<]*(</a>)",
            m =>
            {
                var openTag = Regex.Replace(m.Groups[1].Value, @"data-title=""[^""]*""", $@"data-title=""{encodedTitle}""");
                return $"{openTag}@{encodedTitle}{m.Groups[2].Value}";
            },
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private static string StripMentionLinksFromContent(string content, Guid noteId)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var idStr = noteId.ToString();
        return Regex.Replace(
            content,
            $@"<a\s[^>]*data-note-id=""{Regex.Escape(idStr)}""[^>]*>(@[^<]*)</a>",
            "$1",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private static string UpdatePageLinkTitleInContent(string content, Guid noteId, string newTitle)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var idStr = noteId.ToString();
        var encodedTitle = WebUtility.HtmlEncode(newTitle);

        return Regex.Replace(
            content,
            $@"<div\s[^>]*data-note-id=""{Regex.Escape(idStr)}""[^>]*>.*?</div>",
            m =>
            {
                var tag = m.Value;
                tag = Regex.Replace(tag, @"data-title=""[^""]*""", $@"data-title=""{encodedTitle}""");
                tag = Regex.Replace(tag, @">.*?</div>$", $">📄 {encodedTitle}</div>", RegexOptions.Singleline);
                return tag;
            },
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns true if making <paramref name="proposedParentId"/> the parent of
    /// <paramref name="noteId"/> would create a cycle in the ancestor chain.
    /// </summary>
    public async Task<bool> HasCircularReference(Guid noteId, Guid proposedParentId, CancellationToken ct = default)
    {
        const int maxDepth = 100;
        var current = (Guid?)proposedParentId;
        for (var depth = 0; current.HasValue && depth < maxDepth; depth++)
        {
            if (current.Value == noteId) return true;
            current = await db.Notes
                .Where(n => n.Id == current.Value)
                .Select(n => n.ParentNoteId)
                .FirstOrDefaultAsync(ct);
        }
        return false;
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
    /// Soft delete: moves the note and all its active descendants to the recycle bin.
    /// References in other notes and uploaded files are left untouched so a
    /// restore is lossless; cleanup is deferred to permanent deletion.
    /// </summary>
    public async Task<bool> Delete(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return false;

        var deletedAt = UtcNowMicroseconds();
        note.DeletedAt = deletedAt;
        note.IsDeletionRoot = true;

        // Active descendants are swept into the same recycle bin group (same timestamp).
        // Descendants soft-deleted earlier keep their own group and restore independently.
        var descendants = await CollectActiveDescendantsAsync(id, ct);
        foreach (var descendant in descendants)
        {
            descendant.DeletedAt = deletedAt;
            descendant.IsDeletionRoot = false;
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

        var groupDeletedAt = note.DeletedAt!.Value;
        var groupMembers = await CollectDeletedGroupDescendantsAsync(id, groupDeletedAt, ct);

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
        foreach (var member in groupMembers)
        {
            member.DeletedAt = null;
            member.IsDeletionRoot = false;
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

        var archivedAt = UtcNowMicroseconds();
        note.ArchivedAt = archivedAt;
        note.IsArchiveRoot = true;

        // Sweep active descendants into the same archive group. The BFS sees
        // archived descendants too (archive has no global filter), so skip them:
        // independently archived subtrees keep their own root and timestamp.
        var descendants = (await CollectActiveDescendantsAsync(id, ct))
            .Where(d => d.ArchivedAt == null)
            .ToList();
        foreach (var descendant in descendants)
        {
            descendant.ArchivedAt = archivedAt;
            descendant.IsArchiveRoot = false;
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

        var groupArchivedAt = note.ArchivedAt!.Value;
        var groupMembers = await CollectArchivedGroupDescendantsAsync(id, groupArchivedAt, ct);

        note.ArchivedAt = null;
        note.IsArchiveRoot = false;
        foreach (var member in groupMembers)
        {
            member.ArchivedAt = null;
            member.IsArchiveRoot = false;
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

        // Direct children swept in the same archive operation (same timestamp),
        // mirroring the recycle bin's per-root child counts.
        var ids = items.Select(i => i.Id).ToList();
        if (ids.Count > 0)
        {
            var children = await db.Notes
                .Where(n => n.ArchivedAt != null
                    && n.ParentNoteId.HasValue
                    && ids.Contains(n.ParentNoteId.Value))
                .Select(n => new { ParentId = n.ParentNoteId!.Value, n.ArchivedAt })
                .ToListAsync(ct);
            foreach (var item in items)
                item.ChildCount = children.Count(c =>
                    c.ParentId == item.Id && c.ArchivedAt == item.ArchivedAt);
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
                    var cleaned = RemovePageLinkFromContent(parent.Content, note.Id);
                    if (cleaned != parent.Content)
                    {
                        parent.Content = cleaned;
                        parent.ContentText = StripHtml(cleaned);
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

    /// <summary>BFS over active descendants only (global filter applies).</summary>
    private async Task<List<NoteItem>> CollectActiveDescendantsAsync(Guid rootId, CancellationToken ct = default)
        => await CollectDescendantsAsync(rootId, db.Notes, ct);

    /// <summary>BFS over all descendants regardless of recycle bin state.</summary>
    private async Task<List<NoteItem>> CollectDescendantsUnfilteredAsync(Guid rootId, CancellationToken ct = default)
        => await CollectDescendantsAsync(rootId, db.Notes.IgnoreQueryFilters(), ct);

    /// <summary>BFS over soft-deleted descendants sharing the given recycle-bin-group timestamp.</summary>
    private async Task<List<NoteItem>> CollectDeletedGroupDescendantsAsync(Guid rootId, DateTime groupDeletedAt, CancellationToken ct = default)
        => await CollectDescendantsAsync(
            rootId,
            db.Notes.IgnoreQueryFilters().Where(n => n.DeletedAt == groupDeletedAt),
            ct);

    /// <summary>BFS over archived descendants sharing the given archive-group timestamp.
    /// Descends only through same-group notes, so independently archived subtrees stay put.</summary>
    private async Task<List<NoteItem>> CollectArchivedGroupDescendantsAsync(Guid rootId, DateTime groupArchivedAt, CancellationToken ct = default)
        => await CollectDescendantsAsync(
            rootId,
            db.Notes.Where(n => n.ArchivedAt == groupArchivedAt),
            ct);

    private static async Task<List<NoteItem>> CollectDescendantsAsync(Guid rootId, IQueryable<NoteItem> source, CancellationToken ct = default)
    {
        const int maxDepth = 100;
        var result = new List<NoteItem>();
        var frontier = new List<Guid> { rootId };
        for (var depth = 0; frontier.Count > 0 && depth < maxDepth; depth++)
        {
            var children = await source
                .Where(n => n.ParentNoteId.HasValue && frontier.Contains(n.ParentNoteId.Value))
                .ToListAsync(ct);
            if (children.Count == 0) break;
            result.AddRange(children);
            frontier = children.Select(c => c.Id).ToList();
        }
        return result;
    }

    private async Task StripMentionReferencesAsync(Guid noteId, CancellationToken ct = default)
    {
        var idStr = noteId.ToString();
        // IgnoreQueryFilters so recycle-bin (soft-deleted) notes referencing this
        // note also get their dead mention links stripped — otherwise a restored
        // note keeps a dead mention pointing at a permanently-deleted note.
        var referencingNotes = await db.Notes.IgnoreQueryFilters()
            .Where(n => n.Content.Contains($"data-note-id=\"{idStr}\""))
            .ToListAsync(ct);

        foreach (var n in referencingNotes)
        {
            var updated = StripMentionLinksFromContent(n.Content, noteId);
            if (updated == n.Content) continue;
            n.Content = updated;
            n.ContentText = StripHtml(updated);
        }

        if (referencingNotes.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private static string RemovePageLinkFromContent(string content, Guid noteId)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var idStr = noteId.ToString();
        return Regex.Replace(
            content,
            $@"<div\s[^>]*data-note-id=""{Regex.Escape(idStr)}""[^>]*>.*?</div>",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private static IQueryable<NoteItem> ApplyFilters(
        IQueryable<NoteItem> query, string? search, Guid[]? labelIds)
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

        if (labelIds is { Length: > 0 })
            foreach (var id in labelIds)
                query = query.Where(n => n.NoteLabels.Any(nl => nl.LabelId == id));

        return query;
    }

    private static string EscapeLikePattern(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex HtmlEntityRegex = new("&[a-zA-Z]+;|&#[0-9]+;", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    // PostgreSQL timestamps are stored at microsecond precision (6 digits), while .NET DateTime
    // has 100-nanosecond precision (7 digits). Truncating before save ensures the value returned
    // from the server matches what the DB stores, preventing false optimistic-concurrency conflicts.
    private static DateTime UtcNowMicroseconds()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks / 10 * 10, DateTimeKind.Utc);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = HtmlTagRegex.Replace(html, " ");
        text = HtmlEntityRegex.Replace(text, " ");
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

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

    private static void PopulateLabels(NoteItem note)
    {
        note.Labels = OrderLabelsForDisplay(
            note.NoteLabels.Select(nl => new LabelSummary
            {
                Id = nl.Label.Id,
                Name = nl.Label.Name,
                CategoryId = nl.Label.CategoryId,
                CategoryName = nl.Label.Category?.Name
            }))
            .ToList();
    }

    /// <summary>
    /// Orders labels for display so category and general labels do not interleave
    /// (issue #186): category labels first, grouped by category name, then general
    /// labels, sorted alphabetically by name within each group.
    /// </summary>
    private static IOrderedEnumerable<LabelSummary> OrderLabelsForDisplay(IEnumerable<LabelSummary> labels) =>
        labels
            .OrderBy(l => l.CategoryId.HasValue ? 0 : 1)
            .ThenBy(l => l.CategoryName)
            .ThenBy(l => l.Name);
}
