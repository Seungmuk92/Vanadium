using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class NoteService(NoteDbContext db, FileCleanupService fileCleanup, ILogger<NoteService> logger)
{
    public async Task<PagedResult<NoteSummary>> GetPaged(
        int page,
        int pageSize,
        string? search,
        string sortBy,
        string sortDir,
        Guid[]? labelIds)
    {
        // When not searching, show root notes only
        bool rootOnly = string.IsNullOrWhiteSpace(search);

        // Lean query for COUNT — no joins to label/category tables
        var countQuery = ApplyFilters(
            rootOnly ? db.Notes.Where(n => n.ParentNoteId == null) : db.Notes.AsQueryable(),
            search, labelIds);
        var totalCount = await countQuery.CountAsync();

        // Full query for data — projects to NoteSummary to avoid fetching large Content column
        var baseDataQuery = ApplyFilters(
            rootOnly ? db.Notes.Where(n => n.ParentNoteId == null) : db.Notes.AsQueryable(),
            search,
            labelIds);

        var orderedQuery = !string.IsNullOrWhiteSpace(search)
            ? baseDataQuery.OrderByDescending(n =>
                EF.Functions.ToTsVector("simple", n.Title + " " + n.ContentText)
                    .Rank(EF.Functions.WebSearchToTsQuery("simple", search)))
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
                Labels = n.NoteLabels.Select(nl => new LabelSummary
                {
                    Id = nl.Label.Id,
                    Name = nl.Label.Name,
                    CategoryId = nl.Label.CategoryId,
                    CategoryName = nl.Label.Category == null ? null : nl.Label.Category.Name
                }).ToList()
            })
            .ToListAsync();

        var childCounts = await GetChildCountsAsync(summaries.Select(n => n.Id));

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
                    .ToDictionaryAsync(n => n.Id, n => n.Title);
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
                Labels = n.Labels.OrderBy(l => l.Name).ToList()
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<NoteSummary>> GetAllSummaries(Guid[]? labelIds = null)
    {
        var query = db.Notes.AsQueryable();

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
            .ToListAsync();

        var childCounts = await GetChildCountsAsync(summaries.Select(n => n.Id));

        logger.LogDebug("GetAllSummaries: {Count} note(s).", summaries.Count);
        return summaries.Select(n => new NoteSummary
        {
            Id = n.Id,
            Title = n.Title,
            UpdatedAt = n.UpdatedAt,
            ParentNoteId = n.ParentNoteId,
            ChildCount = childCounts.GetValueOrDefault(n.Id),
            Labels = n.Labels.OrderBy(l => l.Name).ToList()
        }).ToList();
    }

    public async Task<List<NoteSummary>> GetChildren(Guid parentId)
    {
        var summaries = await db.Notes
            .Where(n => n.ParentNoteId == parentId)
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
            .ToListAsync();

        var childCounts = await GetChildCountsAsync(summaries.Select(n => n.Id));

        logger.LogDebug("GetChildren: parentId={ParentId}, count={Count}.", parentId, summaries.Count);
        return summaries.Select(n => new NoteSummary
        {
            Id = n.Id,
            Title = n.Title,
            UpdatedAt = n.UpdatedAt,
            ParentNoteId = n.ParentNoteId,
            ChildCount = childCounts.GetValueOrDefault(n.Id),
            Labels = n.Labels.OrderBy(l => l.Name).ToList()
        }).ToList();
    }

    public async Task<NoteItem?> Get(Guid id)
    {
        var note = await db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is null) return null;

        PopulateLabels(note);
        note.ChildCount = await db.Notes.CountAsync(n => n.ParentNoteId == id);

        if (note.ParentNoteId.HasValue)
        {
            note.ParentTitle = await db.Notes
                .Where(n => n.Id == note.ParentNoteId.Value)
                .Select(n => n.Title)
                .FirstOrDefaultAsync();
        }

        return note;
    }

    public async Task<NoteItem> Create(NoteItem note)
    {
        note.Id = Guid.NewGuid();
        note.UpdatedAt = DateTime.UtcNow;
        note.ContentText = StripHtml(note.Content);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note;
    }

    public async Task<(NoteItem? Note, bool Conflict)> Update(Guid id, NoteItem note)
    {
        var existing = await db.Notes.FindAsync(id);
        if (existing is null) return (null, false);

        // Optimistic concurrency: reject if the client's known version differs from DB,
        // unless UpdatedAt is default (force-save bypass).
        if (note.UpdatedAt != default && existing.UpdatedAt != note.UpdatedAt)
        {
            logger.LogWarning(
                "Conflict on note {NoteId}: client has {ClientVersion}, server has {ServerVersion}.",
                id, note.UpdatedAt, existing.UpdatedAt);
            return (null, true);
        }

        var titleChanged = existing.Title != note.Title;

        existing.Title = note.Title;
        existing.Content = note.Content;
        existing.ContentText = StripHtml(note.Content);
        existing.ParentNoteId = note.ParentNoteId;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (titleChanged)
            await UpdatePageLinkReferences(id, note.Title);

        return (existing, false);
    }

    private async Task UpdatePageLinkReferences(Guid noteId, string newTitle)
    {
        var idStr = noteId.ToString();
        var referencingNotes = await db.Notes
            .Where(n => n.Content.Contains($"data-note-id=\"{idStr}\""))
            .ToListAsync();

        if (referencingNotes.Count == 0) return;

        foreach (var n in referencingNotes)
        {
            var updated = UpdatePageLinkTitleInContent(n.Content, noteId, newTitle);
            if (updated == n.Content) continue;
            n.Content = updated;
            n.ContentText = StripHtml(updated);
        }

        await db.SaveChangesAsync();
        logger.LogInformation(
            "Propagated title change to '{NewTitle}' across {Count} note(s) referencing {NoteId}.",
            newTitle, referencingNotes.Count, noteId);
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
    public async Task<bool> HasCircularReference(Guid noteId, Guid proposedParentId)
    {
        const int maxDepth = 100;
        var current = (Guid?)proposedParentId;
        for (var depth = 0; current.HasValue && depth < maxDepth; depth++)
        {
            if (current.Value == noteId) return true;
            current = await db.Notes
                .Where(n => n.Id == current.Value)
                .Select(n => n.ParentNoteId)
                .FirstOrDefaultAsync();
        }
        return false;
    }

    public async Task<bool> Delete(Guid id)
    {
        var note = await db.Notes.FindAsync(id);
        if (note is null) return false;

        // Remove any page-link blocks referencing this note from the parent's content
        if (note.ParentNoteId.HasValue)
        {
            var parent = await db.Notes.FindAsync(note.ParentNoteId.Value);
            if (parent is not null)
            {
                var cleaned = RemovePageLinkFromContent(parent.Content, id);
                if (cleaned != parent.Content)
                {
                    parent.Content = cleaned;
                    parent.ContentText = StripHtml(cleaned);
                }
            }
        }

        var content = note.Content;
        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        await fileCleanup.DeleteOrphanedFromContentAsync(content);
        return true;
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
            query = query.Where(n =>
                EF.Functions.ToTsVector("simple", n.Title + " " + n.ContentText)
                    .Matches(EF.Functions.WebSearchToTsQuery("simple", search)));

        if (labelIds is { Length: > 0 })
            foreach (var id in labelIds)
                query = query.Where(n => n.NoteLabels.Any(nl => nl.LabelId == id));

        return query;
    }

    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex HtmlEntityRegex = new("&[a-zA-Z]+;|&#[0-9]+;", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = HtmlTagRegex.Replace(html, " ");
        text = HtmlEntityRegex.Replace(text, " ");
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    private async Task<Dictionary<Guid, int>> GetChildCountsAsync(IEnumerable<Guid> noteIds)
    {
        var ids = noteIds.ToList();
        if (ids.Count == 0) return [];
        return await db.Notes
            .Where(n => n.ParentNoteId.HasValue && ids.Contains(n.ParentNoteId!.Value))
            .GroupBy(n => n.ParentNoteId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count);
    }

    private static void PopulateLabels(NoteItem note)
    {
        note.Labels = note.NoteLabels
            .Select(nl => new LabelSummary
            {
                Id = nl.Label.Id,
                Name = nl.Label.Name,
                CategoryId = nl.Label.CategoryId,
                CategoryName = nl.Label.Category?.Name
            })
            .OrderBy(l => l.Name)
            .ToList();
    }
}
