using System.Net.Http.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class LabelService(HttpClient http)
{
    public Task<List<Label>?> GetLabelsAsync() =>
        http.GetFromJsonAsync<List<Label>>("api/labels");

    public async Task<Label?> CreateLabelAsync(string name, Guid? categoryId)
    {
        var response = await http.PostAsJsonAsync("api/labels", new { name, categoryId });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<Label>()
            : null;
    }

    public Task DeleteLabelAsync(Guid id) =>
        http.DeleteAsync($"api/labels/{id}");

    public Task<List<LabelCategory>?> GetCategoriesAsync() =>
        http.GetFromJsonAsync<List<LabelCategory>>("api/label-categories");

    public async Task<LabelCategory?> CreateCategoryAsync(string name)
    {
        var response = await http.PostAsJsonAsync("api/label-categories", new { name });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<LabelCategory>()
            : null;
    }

    public Task DeleteCategoryAsync(Guid id) =>
        http.DeleteAsync($"api/label-categories/{id}");

    public Task AddLabelToNoteAsync(Guid noteId, Guid labelId) =>
        http.PostAsJsonAsync($"api/notes/{noteId}/labels", new { labelId });

    public Task RemoveLabelFromNoteAsync(Guid noteId, Guid labelId) =>
        http.DeleteAsync($"api/notes/{noteId}/labels/{labelId}");
}
