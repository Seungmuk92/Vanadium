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
        // Lean query for COUNT — no joins to label/category tables
        var countQuery = ApplyFilters(db.Notes.AsQueryable(), search, labelIds);
        var totalCount = await countQuery.CountAsync();

        // Full query for data — includes needed for label projection
        var dataQuery = ApplyFilters(
            db.Notes
                .Include(n => n.NoteLabels)
                .ThenInclude(nl => nl.Label)
                .ThenInclude(l => l.Category)
                .AsQueryable(),
            search,
            labelIds);

        dataQuery = (sortBy.ToLowerInvariant(), sortDir.ToLowerInvariant()) switch
        {
            ("title", "asc")  => dataQuery.OrderBy(n => n.Title),
            ("title", "desc") => dataQuery.OrderByDescending(n => n.Title),
            ("date",  "asc")  => dataQuery.OrderBy(n => n.UpdatedAt),
            _                 => dataQuery.OrderByDescending(n => n.UpdatedAt)
        };

        var notes = await dataQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        logger.LogDebug("GetPaged: page={Page}, pageSize={PageSize}, total={Total}.", page, pageSize, totalCount);

        return new PagedResult<NoteSummary>
        {
            Items = notes.Select(ToSummary).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<NoteSummary>> GetAllSummaries(Guid[]? labelIds = null)
    {
        var query = db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .AsQueryable();

        // OR logic: notes that have ANY of the specified labels
        if (labelIds is { Length: > 0 })
            query = query.Where(n => n.NoteLabels.Any(nl => labelIds.Contains(nl.LabelId)));

        var notes = await query
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

        logger.LogDebug("GetAllSummaries: {Count} note(s).", notes.Count);
        return notes.Select(ToSummary).ToList();
    }

    public async Task<NoteItem?> Get(Guid id)
    {
        var note = await db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is not null)
            PopulateLabels(note);

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

    public async Task<NoteItem?> Update(Guid id, NoteItem note)
    {
        var existing = await db.Notes.FindAsync(id);
        if (existing is null) return null;

        existing.Title = note.Title;
        existing.Content = note.Content;
        existing.ContentText = StripHtml(note.Content);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> Delete(Guid id)
    {
        var note = await db.Notes.FindAsync(id);
        if (note is null) return false;

        var content = note.Content;
        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        await fileCleanup.DeleteOrphanedFromContentAsync(content);
        return true;
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

    private static NoteSummary ToSummary(NoteItem note) => new()
    {
        Id = note.Id,
        Title = note.Title,
        UpdatedAt = note.UpdatedAt,
        Labels = note.NoteLabels
            .Select(nl => new LabelSummary
            {
                Id = nl.Label.Id,
                Name = nl.Label.Name,
                CategoryId = nl.Label.CategoryId,
                CategoryName = nl.Label.Category?.Name
            })
            .OrderBy(l => l.Name)
            .ToList()
    };

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
