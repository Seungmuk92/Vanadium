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
        var query = db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(n => n.Title.Contains(search));

        if (labelIds is { Length: > 0 })
        {
            foreach (var id in labelIds)
                query = query.Where(n => n.NoteLabels.Any(nl => nl.LabelId == id));
        }

        query = (sortBy.ToLowerInvariant(), sortDir.ToLowerInvariant()) switch
        {
            ("title", "asc")  => query.OrderBy(n => n.Title),
            ("title", "desc") => query.OrderByDescending(n => n.Title),
            ("date",  "asc")  => query.OrderBy(n => n.UpdatedAt),
            _                 => query.OrderByDescending(n => n.UpdatedAt)
        };

        var totalCount = await query.CountAsync();

        var notes = await query
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

    public async Task<List<NoteSummary>> GetAllSummaries()
    {
        var notes = await db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
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
