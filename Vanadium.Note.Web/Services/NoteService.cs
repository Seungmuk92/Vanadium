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
            var response = await http.GetAsync($"api/notes/{id}");

            // A genuine 404 means the note no longer exists — callers may safely
            // prune stale references. Any other non-success (5xx, network handled
            // below) is transient and must NOT be treated as a deletion.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ServiceResult<NoteItem>.NotFound("Note not found.");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to load note {NoteId}: {StatusCode}.", id, (int)response.StatusCode);
                return ServiceResult<NoteItem>.Fail("Failed to load note.");
            }

            var result = await response.Content.ReadFromJsonAsync<NoteItem>();
            return result is not null
                ? ServiceResult<NoteItem>.Ok(result)
                : ServiceResult<NoteItem>.Fail("Failed to load note.");
        }
        catch (Exception ex)
        {
            // Network failure / timeout — transient, not a 404.
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
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ServiceResult<NoteItem>.Forbidden();
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

    public async Task<ServiceResult<PagedResult<RecycleBinNoteSummary>>> GetRecycleBinAsync(
        int page = 1, int pageSize = 50)
    {
        try
        {
            var result = await http.GetFromJsonAsync<PagedResult<RecycleBinNoteSummary>>(
                $"api/notes/recycle-bin?page={page}&pageSize={pageSize}");
            return result is not null
                ? ServiceResult<PagedResult<RecycleBinNoteSummary>>.Ok(result)
                : ServiceResult<PagedResult<RecycleBinNoteSummary>>.Fail("Failed to load recycle bin.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load recycle bin.");
            return ServiceResult<PagedResult<RecycleBinNoteSummary>>.Fail("Failed to load recycle bin.");
        }
    }

    public async Task<ServiceResult<bool>> RestoreAsync(Guid id)
    {
        try
        {
            var response = await http.PostAsync($"api/notes/{id}/restore", null);
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to restore note.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to restore note {NoteId}.", id);
            return ServiceResult<bool>.Fail("Failed to restore note.");
        }
    }

    public async Task<ServiceResult<bool>> DeletePermanentAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"api/notes/{id}/permanent");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to permanently delete note.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to permanently delete note {NoteId}.", id);
            return ServiceResult<bool>.Fail("Failed to permanently delete note.");
        }
    }

    public async Task<ServiceResult<bool>> EmptyRecycleBinAsync()
    {
        try
        {
            var response = await http.DeleteAsync("api/notes/recycle-bin");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to empty recycle bin.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to empty recycle bin.");
            return ServiceResult<bool>.Fail("Failed to empty recycle bin.");
        }
    }

    public async Task<ServiceResult<bool>> ArchiveAsync(Guid id)
    {
        try
        {
            var response = await http.PostAsync($"api/notes/{id}/archive", null);
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to archive note.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to archive note {NoteId}.", id);
            return ServiceResult<bool>.Fail("Failed to archive note.");
        }
    }

    public async Task<ServiceResult<bool>> UnarchiveAsync(Guid id)
    {
        try
        {
            var response = await http.PostAsync($"api/notes/{id}/unarchive", null);
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to unarchive note.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unarchive note {NoteId}.", id);
            return ServiceResult<bool>.Fail("Failed to unarchive note.");
        }
    }

    public async Task<ServiceResult<PagedResult<ArchivedNoteSummary>>> GetArchiveAsync(
        int page = 1, int pageSize = 50)
    {
        try
        {
            var result = await http.GetFromJsonAsync<PagedResult<ArchivedNoteSummary>>(
                $"api/notes/archive?page={page}&pageSize={pageSize}");
            return result is not null
                ? ServiceResult<PagedResult<ArchivedNoteSummary>>.Ok(result)
                : ServiceResult<PagedResult<ArchivedNoteSummary>>.Fail("Failed to load archive.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load archive.");
            return ServiceResult<PagedResult<ArchivedNoteSummary>>.Fail("Failed to load archive.");
        }
    }

    public async Task<ServiceResult<List<MentionSuggestion>>> SearchForMentionAsync(string query)
    {
        try
        {
            var url = $"api/notes/mention-search?q={Uri.EscapeDataString(query)}";
            var result = await http.GetFromJsonAsync<List<MentionSuggestion>>(url);
            return result is not null
                ? ServiceResult<List<MentionSuggestion>>.Ok(result)
                : ServiceResult<List<MentionSuggestion>>.Fail("No results.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search notes for mention.");
            return ServiceResult<List<MentionSuggestion>>.Fail("Failed to search.");
        }
    }

    public async Task<ServiceResult<List<QuickNavResult>>> QuickSearchAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"api/notes/quick-search?q={Uri.EscapeDataString(query)}&limit={limit}";
            var result = await http.GetFromJsonAsync<List<QuickNavResult>>(url, cancellationToken);
            return ServiceResult<List<QuickNavResult>>.Ok(result ?? []);
        }
        catch (OperationCanceledException)
        {
            throw; // superseded by the next keystroke
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quick search failed.");
            return ServiceResult<List<QuickNavResult>>.Fail("Search failed.");
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
