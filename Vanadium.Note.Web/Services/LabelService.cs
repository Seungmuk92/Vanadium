using System.Net.Http.Json;
using System.Text.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class LabelService(HttpClient http, ILogger<LabelService> logger)
{
    public async Task<ServiceResult<List<Label>>> GetLabelsAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<Label>>("api/labels");
            return result is not null
                ? ServiceResult<List<Label>>.Ok(result)
                : ServiceResult<List<Label>>.Fail("Failed to load labels.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load labels.");
            return ServiceResult<List<Label>>.Fail("Failed to load labels.");
        }
    }

    public async Task<ServiceResult<Label>> CreateLabelAsync(string name, Guid? categoryId)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/labels", new { name, categoryId });
            if (response.IsSuccessStatusCode)
            {
                var label = await response.Content.ReadFromJsonAsync<Label>();
                return label is not null
                    ? ServiceResult<Label>.Ok(label)
                    : ServiceResult<Label>.Fail("An error occurred.");
            }

            var error = await ReadErrorAsync(response);
            logger.LogWarning("Failed to create label '{Name}': {Error}", name, error);
            return ServiceResult<Label>.Fail(error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create label '{Name}'.", name);
            return ServiceResult<Label>.Fail("An error occurred.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteLabelAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"api/labels/{id}");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to delete label.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete label {LabelId}.", id);
            return ServiceResult<bool>.Fail("Failed to delete label.");
        }
    }

    public async Task<ServiceResult<List<LabelCategory>>> GetCategoriesAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<LabelCategory>>("api/label-categories");
            return result is not null
                ? ServiceResult<List<LabelCategory>>.Ok(result)
                : ServiceResult<List<LabelCategory>>.Fail("Failed to load categories.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load label categories.");
            return ServiceResult<List<LabelCategory>>.Fail("Failed to load categories.");
        }
    }

    public async Task<ServiceResult<LabelCategory>> CreateCategoryAsync(string name)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/label-categories", new { name });
            if (response.IsSuccessStatusCode)
            {
                var cat = await response.Content.ReadFromJsonAsync<LabelCategory>();
                return cat is not null
                    ? ServiceResult<LabelCategory>.Ok(cat)
                    : ServiceResult<LabelCategory>.Fail("An error occurred.");
            }

            var error = await ReadErrorAsync(response);
            logger.LogWarning("Failed to create category '{Name}': {Error}", name, error);
            return ServiceResult<LabelCategory>.Fail(error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create category '{Name}'.", name);
            return ServiceResult<LabelCategory>.Fail("An error occurred.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteCategoryAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"api/label-categories/{id}");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to delete category.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category {CategoryId}.", id);
            return ServiceResult<bool>.Fail("Failed to delete category.");
        }
    }

    public async Task<ServiceResult<bool>> AddLabelToNoteAsync(Guid noteId, Guid labelId)
    {
        try
        {
            var response = await http.PostAsJsonAsync($"api/notes/{noteId}/labels", new { labelId });
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to add label.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add label {LabelId} to note {NoteId}.", labelId, noteId);
            return ServiceResult<bool>.Fail("Failed to add label.");
        }
    }

    public async Task<ServiceResult<bool>> RemoveLabelFromNoteAsync(Guid noteId, Guid labelId)
    {
        try
        {
            var response = await http.DeleteAsync($"api/notes/{noteId}/labels/{labelId}");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to remove label.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove label {LabelId} from note {NoteId}.", labelId, noteId);
            return ServiceResult<bool>.Fail("Failed to remove label.");
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
