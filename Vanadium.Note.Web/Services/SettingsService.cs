using System.Net;
using System.Net.Http.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class SettingsService(HttpClient http, ILogger<SettingsService> logger)
{
    public async Task<ServiceResult<UserSettings>> GetAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<UserSettings>("api/settings");
            return result is not null
                ? ServiceResult<UserSettings>.Ok(result)
                : ServiceResult<UserSettings>.Fail("Failed to load settings.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load settings.");
            return ServiceResult<UserSettings>.Fail("Failed to load settings.");
        }
    }

    public async Task<ServiceResult<UserSettings>> SaveAsync(UserSettings settings)
    {
        try
        {
            var response = await http.PutAsJsonAsync("api/settings", settings);
            if (!response.IsSuccessStatusCode)
                return ServiceResult<UserSettings>.Fail("Failed to save settings.");
            var saved = await response.Content.ReadFromJsonAsync<UserSettings>();
            return saved is not null
                ? ServiceResult<UserSettings>.Ok(saved)
                : ServiceResult<UserSettings>.Fail("Failed to save settings.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save settings.");
            return ServiceResult<UserSettings>.Fail("Failed to save settings.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAllDataAsync(string password)
    {
        try
        {
            // The purge endpoint requires the owner password in the body (issue #289), so it
            // is sent as a DELETE with a JSON body rather than a bare DeleteAsync.
            var request = new HttpRequestMessage(HttpMethod.Delete, "api/settings/all-data")
            {
                Content = JsonContent.Create(new DeleteAllDataRequest(password))
            };
            var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return ServiceResult<bool>.Ok(true);
            // A wrong password is rejected with 403; surface that distinctly so the user knows
            // their data is intact and the password was the problem.
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ServiceResult<bool>.Fail("Incorrect password. Your data was not deleted.");
            return ServiceResult<bool>.Fail("Failed to delete data.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete all data.");
            return ServiceResult<bool>.Fail("Failed to delete data.");
        }
    }
}
