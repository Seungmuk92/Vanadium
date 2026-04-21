using System.Net.Http.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class NoteService(HttpClient http, ILogger<NoteService> logger)
{
    public async Task<IReadOnlyList<NoteItem>?> GetAllAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<IReadOnlyList<NoteItem>>("api/notes");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load notes list.");
            return null;
        }
    }

    public async Task<NoteItem?> GetAsync(Guid id)
    {
        try
        {
            return await http.GetFromJsonAsync<NoteItem>($"api/notes/{id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load note {NoteId}.", id);
            return null;
        }
    }

    public async Task<NoteItem?> SaveAsync(NoteItem note)
    {
        try
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save note {NoteId}.", note.Id);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"api/notes/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete note {NoteId}.", id);
            return false;
        }
    }
}
