using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController(SettingsService settingsService) : ControllerBase
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
}
