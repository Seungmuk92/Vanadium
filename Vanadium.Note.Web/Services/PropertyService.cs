using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// HTTP client for Note Properties (issue #343). Definitions are cached per circuit (the panel,
/// filter bar, and sort menu all read from the cache) and the cache is invalidated after any
/// definition/option mutation.
/// </summary>
public class PropertyService(HttpClient http, ILogger<PropertyService> logger)
{
    private List<PropertyDefinition>? _cache;

    /// <summary>Cached definitions (without usage counts). Fetched once per circuit; call
    /// <see cref="InvalidateCache"/> after a mutation to force a refresh.</summary>
    public async Task<ServiceResult<List<PropertyDefinition>>> GetDefinitionsAsync(bool includeUsage = false)
    {
        if (!includeUsage && _cache is not null)
            return ServiceResult<List<PropertyDefinition>>.Ok(_cache);

        try
        {
            var url = includeUsage ? "api/properties?includeUsage=true" : "api/properties";
            var result = await http.GetFromJsonAsync<List<PropertyDefinition>>(url);
            if (result is null)
                return ServiceResult<List<PropertyDefinition>>.Fail("Failed to load properties.");
            if (!includeUsage)
                _cache = result;
            return ServiceResult<List<PropertyDefinition>>.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load property definitions.");
            return ServiceResult<List<PropertyDefinition>>.Fail("Failed to load properties.");
        }
    }

    public void InvalidateCache() => _cache = null;

    public async Task<ServiceResult<PropertyDefinition>> CreateDefinitionAsync(CreatePropertyDefinitionRequest req)
    {
        var result = await PostForAsync<PropertyDefinition>("api/properties", req, "create property");
        if (result.IsSuccess) InvalidateCache();
        return result;
    }

    public async Task<ServiceResult<PropertyDefinition>> UpdateDefinitionAsync(Guid id, UpdatePropertyDefinitionRequest req)
    {
        var result = await PutForAsync<PropertyDefinition>($"api/properties/{id}", req, "update property");
        if (result.IsSuccess) InvalidateCache();
        return result;
    }

    public async Task<ServiceResult<bool>> DeleteDefinitionAsync(Guid id)
    {
        var result = await DeleteForAsync($"api/properties/{id}", "delete property");
        if (result.IsSuccess) InvalidateCache();
        return result;
    }

    public async Task<ServiceResult<PropertyOption>> AddOptionAsync(Guid definitionId, CreatePropertyOptionRequest req)
    {
        var result = await PostForAsync<PropertyOption>($"api/properties/{definitionId}/options", req, "add option");
        if (result.IsSuccess) InvalidateCache();
        return result;
    }

    public async Task<ServiceResult<PropertyOption>> UpdateOptionAsync(Guid definitionId, Guid optionId, UpdatePropertyOptionRequest req)
    {
        var result = await PutForAsync<PropertyOption>($"api/properties/{definitionId}/options/{optionId}", req, "update option");
        if (result.IsSuccess) InvalidateCache();
        return result;
    }

    public async Task<ServiceResult<bool>> DeleteOptionAsync(Guid definitionId, Guid optionId)
    {
        var result = await DeleteForAsync($"api/properties/{definitionId}/options/{optionId}", "delete option");
        if (result.IsSuccess) InvalidateCache();
        return result;
    }

    public async Task<ServiceResult<NotePropertyValue>> SetValueAsync(Guid noteId, Guid definitionId, SetNotePropertyValueRequest req)
    {
        try
        {
            var response = await http.PutAsJsonAsync($"api/notes/{noteId}/properties/{definitionId}", req);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ServiceResult<NotePropertyValue>.Forbidden();
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ServiceResult<NotePropertyValue>.NotFound("Note or property no longer exists.");
            if (!response.IsSuccessStatusCode)
                return ServiceResult<NotePropertyValue>.Fail(await ReadErrorAsync(response));
            var value = await response.Content.ReadFromJsonAsync<NotePropertyValue>();
            return value is not null
                ? ServiceResult<NotePropertyValue>.Ok(value)
                : ServiceResult<NotePropertyValue>.Fail("Failed to save property value.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set property {DefinitionId} on note {NoteId}.", definitionId, noteId);
            return ServiceResult<NotePropertyValue>.Fail("Failed to save property value.");
        }
    }

    public async Task<ServiceResult<bool>> ClearValueAsync(Guid noteId, Guid definitionId)
    {
        try
        {
            var response = await http.DeleteAsync($"api/notes/{noteId}/properties/{definitionId}");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ServiceResult<bool>.Forbidden();
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ServiceResult<bool>.NotFound("Note or property no longer exists.");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to clear property value.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear property {DefinitionId} on note {NoteId}.", definitionId, noteId);
            return ServiceResult<bool>.Fail("Failed to clear property value.");
        }
    }

    // ── Shared request helpers ───────────────────────────────────────────────────

    private async Task<ServiceResult<T>> PostForAsync<T>(string url, object body, string action)
    {
        try
        {
            var response = await http.PostAsJsonAsync(url, body);
            return await ReadResultAsync<T>(response, action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}.", action);
            return ServiceResult<T>.Fail("An error occurred.");
        }
    }

    private async Task<ServiceResult<T>> PutForAsync<T>(string url, object body, string action)
    {
        try
        {
            var response = await http.PutAsJsonAsync(url, body);
            return await ReadResultAsync<T>(response, action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}.", action);
            return ServiceResult<T>.Fail("An error occurred.");
        }
    }

    private async Task<ServiceResult<bool>> DeleteForAsync(string url, string action)
    {
        try
        {
            var response = await http.DeleteAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ServiceResult<bool>.NotFound("Not found.");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail(await ReadErrorAsync(response));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to {Action}.", action);
            return ServiceResult<bool>.Fail("An error occurred.");
        }
    }

    private async Task<ServiceResult<T>> ReadResultAsync<T>(HttpResponseMessage response, string action)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
            return ServiceResult<T>.Fail(await ReadErrorAsync(response));
        if (response.StatusCode == HttpStatusCode.NotFound)
            return ServiceResult<T>.NotFound("Not found.");
        if (!response.IsSuccessStatusCode)
            return ServiceResult<T>.Fail(await ReadErrorAsync(response));
        var value = await response.Content.ReadFromJsonAsync<T>();
        return value is not null
            ? ServiceResult<T>.Ok(value)
            : ServiceResult<T>.Fail($"Failed to {action}.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString() ?? "An error occurred.";
        }
        catch { }
        return "An error occurred.";
    }
}
