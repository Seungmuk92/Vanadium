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
    private string Username => User.Identity!.Name!;

    [HttpGet]
    public async Task<ActionResult<UserSettings>> Get()
    {
        return Ok(await settingsService.GetAsync(Username));
    }

    [HttpPut]
    public async Task<ActionResult<UserSettings>> Update([FromBody] UserSettings settings)
    {
        return Ok(await settingsService.UpsertAsync(Username, settings));
    }

    /// <summary>
    /// Permanently deletes all content created by the current user — notes,
    /// labels, label categories, API tokens, settings, and orphaned uploads.
    /// The account itself is kept so the user remains able to log in.
    /// </summary>
    [HttpDelete("all-data")]
    public async Task<IActionResult> DeleteAllData()
    {
        logger.LogWarning("All-data purge requested by {Username}.", Username);
        var purged = await accountService.PurgeAllDataAsync(Username, HttpContext.RequestAborted);
        return purged ? NoContent() : NotFound();
    }
}
