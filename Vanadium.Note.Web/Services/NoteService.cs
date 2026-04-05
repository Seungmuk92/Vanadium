using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class NoteService
{
    private readonly List<NoteItem> _notes = [];

    public IReadOnlyList<NoteItem> GetAll() =>
        _notes.OrderByDescending(n => n.UpdatedAt).ToList();

    public NoteItem? Get(Guid id) =>
        _notes.FirstOrDefault(n => n.Id == id);

    public void Save(NoteItem note)
    {
        var existing = _notes.FirstOrDefault(n => n.Id == note.Id);
        if (existing is null)
        {
            _notes.Add(note);
        }
        else
        {
            existing.Title = note.Title;
            existing.Content = note.Content;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }

    public void Delete(Guid id) =>
        _notes.RemoveAll(n => n.Id == id);
}
