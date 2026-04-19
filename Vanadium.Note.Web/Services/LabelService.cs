using System.Net.Http.Json;
using System.Text.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class LabelService(HttpClient http)
{
    public Task<List<Label>?> GetLabelsAsync() =>
        http.GetFromJsonAsync<List<Label>>("api/labels");

    public async Task<(Label? Result, string? Error)> CreateLabelAsync(string name, Guid? categoryId)
    {
        var response = await http.PostAsJsonAsync("api/labels", new { name, categoryId });
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<Label>(), null);
        return (null, await ReadErrorAsync(response));
    }

    public Task DeleteLabelAsync(Guid id) =>
        http.DeleteAsync($"api/labels/{id}");

    public Task<List<LabelCategory>?> GetCategoriesAsync() =>
        http.GetFromJsonAsync<List<LabelCategory>>("api/label-categories");

    public async Task<(LabelCategory? Result, string? Error)> CreateCategoryAsync(string name)
    {
        var response = await http.PostAsJsonAsync("api/label-categories", new { name });
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<LabelCategory>(), null);
        return (null, await ReadErrorAsync(response));
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

    public Task DeleteCategoryAsync(Guid id) =>
        http.DeleteAsync($"api/label-categories/{id}");

    public Task AddLabelToNoteAsync(Guid noteId, Guid labelId) =>
        http.PostAsJsonAsync($"api/notes/{noteId}/labels", new { labelId });

    public Task RemoveLabelFromNoteAsync(Guid noteId, Guid labelId) =>
        http.DeleteAsync($"api/notes/{noteId}/labels/{labelId}");
}
