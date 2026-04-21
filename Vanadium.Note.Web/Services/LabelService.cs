using System.Net.Http.Json;
using System.Text.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class LabelService(HttpClient http, ILogger<LabelService> logger)
{
    public async Task<List<Label>?> GetLabelsAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<Label>>("api/labels");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load labels.");
            return null;
        }
    }

    public async Task<(Label? Result, string? Error)> CreateLabelAsync(string name, Guid? categoryId)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/labels", new { name, categoryId });
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<Label>(), null);

            var error = await ReadErrorAsync(response);
            logger.LogWarning("Failed to create label '{Name}': {Error}", name, error);
            return (null, error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create label '{Name}'.", name);
            return (null, "An error occurred.");
        }
    }

    public async Task DeleteLabelAsync(Guid id)
    {
        try
        {
            await http.DeleteAsync($"api/labels/{id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete label {LabelId}.", id);
        }
    }

    public async Task<List<LabelCategory>?> GetCategoriesAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<LabelCategory>>("api/label-categories");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load label categories.");
            return null;
        }
    }

    public async Task<(LabelCategory? Result, string? Error)> CreateCategoryAsync(string name)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/label-categories", new { name });
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<LabelCategory>(), null);

            var error = await ReadErrorAsync(response);
            logger.LogWarning("Failed to create category '{Name}': {Error}", name, error);
            return (null, error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create category '{Name}'.", name);
            return (null, "An error occurred.");
        }
    }

    public async Task DeleteCategoryAsync(Guid id)
    {
        try
        {
            await http.DeleteAsync($"api/label-categories/{id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category {CategoryId}.", id);
        }
    }

    public async Task AddLabelToNoteAsync(Guid noteId, Guid labelId)
    {
        try
        {
            await http.PostAsJsonAsync($"api/notes/{noteId}/labels", new { labelId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add label {LabelId} to note {NoteId}.", labelId, noteId);
        }
    }

    public async Task RemoveLabelFromNoteAsync(Guid noteId, Guid labelId)
    {
        try
        {
            await http.DeleteAsync($"api/notes/{noteId}/labels/{labelId}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove label {LabelId} from note {NoteId}.", labelId, noteId);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("error", out var err))
                return err.GetString() ?? "An error occurred.";
        }
        catch { }
        return "An error occurred.";
    }
}
