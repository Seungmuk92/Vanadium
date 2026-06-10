using System.Net.Http.Json;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

public class ApiTokenService(HttpClient http, ILogger<ApiTokenService> logger)
{
    public async Task<ServiceResult<List<ApiTokenSummary>>> ListAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<ApiTokenSummary>>("api/apitokens");
            return result is not null
                ? ServiceResult<List<ApiTokenSummary>>.Ok(result)
                : ServiceResult<List<ApiTokenSummary>>.Fail("Failed to load API tokens.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load API tokens.");
            return ServiceResult<List<ApiTokenSummary>>.Fail("Failed to load API tokens.");
        }
    }

    public async Task<ServiceResult<CreateApiTokenResponse>> CreateAsync(CreateApiTokenRequest request)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/apitokens", request);
            if (!response.IsSuccessStatusCode)
                return ServiceResult<CreateApiTokenResponse>.Fail("Failed to create API token.");
            var created = await response.Content.ReadFromJsonAsync<CreateApiTokenResponse>();
            return created is not null
                ? ServiceResult<CreateApiTokenResponse>.Ok(created)
                : ServiceResult<CreateApiTokenResponse>.Fail("Failed to create API token.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create API token.");
            return ServiceResult<CreateApiTokenResponse>.Fail("Failed to create API token.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"api/apitokens/{id}");
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail("Failed to delete API token.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete API token.");
            return ServiceResult<bool>.Fail("Failed to delete API token.");
        }
    }
}
