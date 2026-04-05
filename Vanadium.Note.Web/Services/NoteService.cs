using System.Net.Http.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class NoteService(HttpClient http)
{
    public Task<IReadOnlyList<NoteItem>?> GetAllAsync() =>
        http.GetFromJsonAsync<IReadOnlyList<NoteItem>>("api/notes");

    public Task<NoteItem?> GetAsync(Guid id) =>
        http.GetFromJsonAsync<NoteItem>($"api/notes/{id}");

    public async Task<NoteItem?> SaveAsync(NoteItem note)
    {
        if (note.Id == Guid.Empty)
        {
            var response = await http.PostAsJsonAsync("api/notes", note);
            return await response.Content.ReadFromJsonAsync<NoteItem>();
        }
        else
        {
            var response = await http.PutAsJsonAsync($"api/notes/{note.Id}", note);
            return await response.Content.ReadFromJsonAsync<NoteItem>();
        }
    }

    public Task DeleteAsync(Guid id) =>
        http.DeleteAsync($"api/notes/{id}");
}
