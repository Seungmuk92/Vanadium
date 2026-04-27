using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotesController(NoteService noteService, LabelService labelService, NoteDbContext db, ILogger<NotesController> logger) : ControllerBase
{
    private async Task<Guid> GetUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim is not null && Guid.TryParse(idClaim, out var userId))
            return userId;

        // Fallback for tokens issued before user ID was added to claims
        var username = User.FindFirst(ClaimTypes.Name)?.Value
            ?? throw new InvalidOperationException("No identity claims in token.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new InvalidOperationException($"User '{username}' not found.");
        return user.Id;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<NoteSummary>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery][MaxLength(200)] string? search = null,
        [FromQuery] string sortBy = "date",
        [FromQuery] string sortDir = "desc",
        [FromQuery] Guid[]? labelIds = null,
        [FromQuery] bool includeLabels = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        if (labelIds is { Length: > 50 })
            return BadRequest("Too many label IDs (maximum 50).");
        var userId = await GetUserId();
        var result = await noteService.GetPaged(userId, page, pageSize, search, sortBy, sortDir, labelIds);
        if (includeLabels)
            result.Labels = await labelService.GetAllLabelsAsync(userId);
        return Ok(result);
    }

    [HttpGet("summaries")]
    public async Task<ActionResult<List<NoteSummary>>> GetSummaries(
        [FromQuery] Guid[]? labelIds = null)
    {
        if (labelIds is { Length: > 50 })
            return BadRequest("Too many label IDs (maximum 50).");
        return Ok(await noteService.GetAllSummaries(await GetUserId(), labelIds));
    }

    [HttpGet("{id:guid}/children")]
    public async Task<ActionResult<List<NoteSummary>>> GetChildren(Guid id)
    {
        return Ok(await noteService.GetChildren(await GetUserId(), id));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Get(Guid id)
    {
        var note = await noteService.Get(await GetUserId(), id);
        if (note is null)
        {
            logger.LogWarning("Note {NoteId} not found", id);
            return NotFound();
        }
        return Ok(note);
    }

    [HttpPost]
    public async Task<ActionResult<NoteItem>> Create([FromBody] NoteItem note)
    {
        var created = await noteService.Create(await GetUserId(), note);
        logger.LogInformation("Note created: {NoteId}", created.Id);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Update(Guid id, [FromBody] NoteItem note)
    {
        if (note.ParentNoteId.HasValue)
        {
            if (note.ParentNoteId.Value == id)
            {
                logger.LogWarning("Update rejected — note {NoteId} cannot be its own parent.", id);
                return BadRequest("A note cannot be its own parent.");
            }
            if (await noteService.HasCircularReference(id, note.ParentNoteId.Value))
            {
                logger.LogWarning("Update rejected — circular parent reference detected for note {NoteId}.", id);
                return BadRequest("Setting this parent would create a circular reference.");
            }
        }

        var (updated, conflict) = await noteService.Update(await GetUserId(), id, note);
        if (conflict)
            return Conflict(new { message = "The note was modified by another session. Reload to get the latest version." });
        if (updated is null)
        {
            logger.LogWarning("Update failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note updated: {NoteId}", id);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await noteService.Delete(await GetUserId(), id))
        {
            logger.LogWarning("Delete failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note deleted: {NoteId}", id);
        return NoContent();
    }
}
