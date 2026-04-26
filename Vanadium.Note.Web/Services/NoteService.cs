using System.Net;
using System.Net.Http.Json;
using System.Text;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class NoteService(HttpClient http, ILogger<NoteService> logger)
{
    public async Task<ServiceResult<PagedResult<NoteSummary>>> GetAllAsync(
        int page = 1,
        int pageSize = 30,
        string? search = null,
        string sortBy = "date",
        string sortDir = "desc",
        IEnumerable<Guid>? labelIds = null,
        bool includeLabels = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sb = new StringBuilder($"api/notes?page={page}&pageSize={pageSize}&sortBy={sortBy}&sortDir={sortDir}");
            if (!string.IsNullOrWhiteSpace(search))
                sb.Append($"&search={Uri.EscapeDataString(search)}");
            if (labelIds is not null)
                foreach (var id in labelIds)
                    sb.Append($"&labelIds={id}");
            if (includeLabels)
                sb.Append("&includeLabels=true");

            var result = await http.GetFromJsonAsync<PagedResult<NoteSummary>>(sb.ToString(), cancellationToken);
            return result is not null
                ? ServiceResult<PagedResult<NoteSummary>>.Ok(result)
                : ServiceResult<PagedResult<NoteSummary>>.Fail("Failed to load notes.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load notes list.");
            return ServiceResult<PagedResult<NoteSummary>>.Fail("Failed to load notes.");
        }
    }

    public async Task<ServiceResult<IReadOnlyList<NoteSummary>>> GetAllSummariesAsync(
        IEnumerable<Guid>? labelIds = null)
    {
        try
        {
            var url = "api/notes/summaries";
            if (labelIds is not null)
            {
                var ids = labelIds.ToList();
                if (ids.Count > 0)
                    url += "?" + string.Join("&", ids.Select(id => $"labelIds={id}"));
            }
            var result = await http.GetFromJsonAsync<IReadOnlyList<NoteSummary>>(url);
            return result is not null
                ? ServiceResult<IReadOnlyList<NoteSummary>>.Ok(result)
                : ServiceResult<IReadOnlyList<NoteSummary>>.Fail("Failed to load note summaries.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load note summaries.");
            return ServiceResult<IReadOnlyList<NoteSummary>>.Fail("Failed to load note summaries.");
        }
    }

    public async Task<ServiceResult<NoteItem>> GetAsync(Guid id)
    {
        try
        {
            var result = await http.GetFromJsonAsync<NoteItem>($"api/notes/{id}");
            return result is not null
                ? ServiceResult<NoteItem>.Ok(result)
                : ServiceResult<NoteItem>.Fail("Note not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load note {NoteId}.", id);
            return ServiceResult<NoteItem>.Fail("Failed to load note.");
        }
    }

    public async Task<ServiceResult<NoteItem>> SaveAsync(NoteItem note)
    {
        try
        {
            var response = note.Id == Guid.Empty
                ? await http.PostAsJsonAsync("api/notes", note)
                : await http.PutAsJsonAsync($"api/notes/{note.Id}", note);

            if (response.StatusCode == HttpStatusCode.Conflict)
                return ServiceResult<NoteItem>.Conflict();
            if (!response.IsSuccessStatusCode)
                return ServiceResult<NoteItem>.Fail("Failed to save note.");

            var saved = await response.Content.ReadFromJsonAsync<NoteItem>();
            return saved is not null
                ? ServiceResult<NoteItem>.Ok(saved)
                : ServiceResult<NoteItem>.Fail("Failed to save note.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save note {NoteId}.", note.Id);
            return ServiceResult<NoteItem>.Fail("Failed to save note.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"api/notes/{id}");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to delete note.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete note {NoteId}.", id);
            return ServiceResult<bool>.Fail("Failed to delete note.");
        }
    }

    public async Task<ServiceResult<List<NoteSummary>>> GetChildrenAsync(Guid parentId)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<NoteSummary>>($"api/notes/{parentId}/children");
            return result is not null
                ? ServiceResult<List<NoteSummary>>.Ok(result)
                : ServiceResult<List<NoteSummary>>.Fail("Failed to load sub-notes.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load sub-notes for note {NoteId}.", parentId);
            return ServiceResult<List<NoteSummary>>.Fail("Failed to load sub-notes.");
        }
    }
}
