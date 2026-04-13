using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class NoteService(NoteDbContext db, FileCleanupService fileCleanup)
{
    public Task<List<NoteItem>> GetAll() =>
        db.Notes.OrderByDescending(n => n.UpdatedAt).ToListAsync();

    public Task<NoteItem?> Get(Guid id) =>
        db.Notes.FindAsync(id).AsTask();

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

        // Capture content before removal so we can clean up referenced files
        var content = note.Content;

        db.Notes.Remove(note);
        await db.SaveChangesAsync();

        // Delete files/images that are no longer referenced by any note
        await fileCleanup.DeleteOrphanedFromContentAsync(content);

        return true;
    }
}
