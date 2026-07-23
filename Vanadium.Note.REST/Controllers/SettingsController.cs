using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Security;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController(
    SettingsService settingsService,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<SettingsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserSettings>> Get()
    {
        return Ok(await settingsService.GetAsync());
    }

    [HttpPut]
    public async Task<ActionResult<UserSettings>> Update([FromBody] UserSettings settings)
    {
        return Ok(await settingsService.UpsertAsync(settings));
    }

    /// <summary>
    /// Permanently deletes all content — notes, labels, label categories, API tokens,
    /// settings, and orphaned uploads. The owner's password lives in configuration,
    /// so login remains possible afterward.
    /// </summary>
    /// <remarks>
    /// This is the most destructive endpoint and cannot be undone, so it is locked down
    /// beyond the shared <c>[Authorize]</c> smart scheme (issue #289): it accepts ONLY the
    /// interactive JWT scheme, so a leaked personal access token cannot call it, and it
    /// re-confirms the owner password from the request body before deleting anything.
    /// </remarks>
    [HttpDelete("all-data")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DeleteAllData([FromBody] DeleteAllDataRequest request)
    {
        var storedHash = configuration["Auth:PasswordHash"];
        if (string.IsNullOrEmpty(storedHash))
        {
            logger.LogError("Auth:PasswordHash is not configured — the all-data purge cannot verify the owner password.");
            return Problem(
                detail: "Server password is not configured.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (!PasswordHasher.Verify(request.Password, storedHash))
        {
            logger.LogWarning("All-data purge rejected: owner-password re-confirmation failed.");
            return Problem(
                detail: "Invalid password.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        logger.LogWarning("All-data purge requested.");
        var purged = await accountService.PurgeAllDataAsync(HttpContext.RequestAborted);
        return purged ? NoContent() : NotFound();
    }
}
