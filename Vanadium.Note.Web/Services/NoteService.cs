using System.Net.Http.Json;
using System.Text;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class NoteService(HttpClient http, ILogger<NoteService> logger)
{
    public async Task<PagedResult<NoteSummary>?> GetAllAsync(
        int page = 1,
        int pageSize = 30,
        string? search = null,
        string sortBy = "date",
        string sortDir = "desc",
        IEnumerable<Guid>? labelIds = null,
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

            return await http.GetFromJsonAsync<PagedResult<NoteSummary>>(sb.ToString(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load notes list.");
            return null;
        }
    }

    public async Task<IReadOnlyList<NoteSummary>?> GetAllSummariesAsync(
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
            return await http.GetFromJsonAsync<IReadOnlyList<NoteSummary>>(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load note summaries.");
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
