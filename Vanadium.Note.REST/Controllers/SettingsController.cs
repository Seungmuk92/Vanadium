using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController(
    SettingsService settingsService,
    AccountService accountService,
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
    [HttpDelete("all-data")]
    public async Task<IActionResult> DeleteAllData()
    {
        logger.LogWarning("All-data purge requested.");
        var purged = await accountService.PurgeAllDataAsync(HttpContext.RequestAborted);
        return purged ? NoContent() : NotFound();
    }
}
