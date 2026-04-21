using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class NoteService(NoteDbContext db, FileCleanupService fileCleanup, ILogger<NoteService> logger)
{
    public async Task<List<NoteItem>> GetAll()
    {
        var notes = await db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

        foreach (var note in notes)
            PopulateLabels(note);

        logger.LogDebug("Retrieved {Count} note(s).", notes.Count);
        return notes;
    }

    public async Task<NoteItem?> Get(Guid id)
    {
        var note = await db.Notes
            .Include(n => n.NoteLabels)
            .ThenInclude(nl => nl.Label)
            .ThenInclude(l => l.Category)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is null)
        {
            logger.LogDebug("Note {NoteId} not found.", id);
            return null;
        }

        PopulateLabels(note);
        return note;
    }

    public async Task<NoteItem> Create(NoteItem note)
    {
        note.Id = Guid.NewGuid();
        note.UpdatedAt = DateTime.UtcNow;
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        logger.LogInformation("Note created: {NoteId}.", note.Id);
        return note;
    }

    public async Task<NoteItem?> Update(Guid id, NoteItem note)
    {
        var existing = await db.Notes.FindAsync(id);
        if (existing is null)
        {
            logger.LogDebug("Note {NoteId} not found for update.", id);
            return null;
        }

        existing.Title = note.Title;
        existing.Content = note.Content;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Note updated: {NoteId}.", id);
        return existing;
    }

    public async Task<bool> Delete(Guid id)
    {
        var note = await db.Notes.FindAsync(id);
        if (note is null)
        {
            logger.LogDebug("Note {NoteId} not found for deletion.", id);
            return false;
        }

        var content = note.Content;

        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        logger.LogInformation("Note deleted: {NoteId}. Running orphan file cleanup.", id);

        await fileCleanup.DeleteOrphanedFromContentAsync(content);

        return true;
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
