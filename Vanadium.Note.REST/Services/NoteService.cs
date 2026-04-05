using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class NoteService
{
    private readonly List<NoteItem> _notes = [];

    public IReadOnlyList<NoteItem> GetAll() =>
        _notes.OrderByDescending(n => n.UpdatedAt).ToList();

    public NoteItem? Get(Guid id) =>
        _notes.FirstOrDefault(n => n.Id == id);

    public NoteItem Create(NoteItem note)
    {
        note.Id = Guid.NewGuid();
        note.UpdatedAt = DateTime.UtcNow;
        _notes.Add(note);
        return note;
    }

    public NoteItem? Update(Guid id, NoteItem note)
    {
        var existing = _notes.FirstOrDefault(n => n.Id == id);
        if (existing is null) return null;

        existing.Title = note.Title;
        existing.Content = note.Content;
        existing.UpdatedAt = DateTime.UtcNow;
        return existing;
    }

    public bool Delete(Guid id) =>
        _notes.RemoveAll(n => n.Id == id) > 0;
}
