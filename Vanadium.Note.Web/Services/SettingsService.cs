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
}
