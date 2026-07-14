using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

/// <summary>
/// Anonymous, read-only access to shared notes. This is the ONLY controller reachable without
/// authentication besides login, so it stays deliberately small: a single GET that resolves a note
/// by its unguessable share token and returns a lean, read-only projection. Rate-limited per IP so
/// the open endpoint cannot be used to brute-force tokens or hammer the database.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("share")]
public class ShareController(NoteService noteService, ILogger<ShareController> logger) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<SharedNote>> Get(string token, CancellationToken ct)
    {
        var note = await noteService.GetSharedByToken(token, ct);
        if (note is null)
        {
            logger.LogInformation("Shared note requested with an unknown or revoked token.");
            return NotFound();
        }

        // Link-only shares should not be indexed by search engines; public shares may be.
        if (note.ShareMode == ShareMode.Link)
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        logger.LogInformation("Shared note {NoteId} served ({ShareMode}).", note.Id, note.ShareMode);
        return Ok(new SharedNote
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            UpdatedAt = note.UpdatedAt
        });
    }
}
