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

    [HttpGet("mention-search")]
    public async Task<ActionResult<List<MentionSuggestionDto>>> MentionSearch(
        [FromQuery][MaxLength(100)] string q = "")
    {
        return Ok(await noteService.SearchForMention(await GetUserId(), q));
    }

    [HttpGet("quick-search")]
    public async Task<ActionResult<List<QuickNavResult>>> QuickSearch(
        [FromQuery][MaxLength(200)] string q = "",
        [FromQuery] int limit = 20)
    {
        var userId = await GetUserId();
        var results = await noteService.QuickSearch(userId, q, limit);
        // Avoid logging the raw query (note content privacy, NFR-6): term + result counts only.
        var termCount = q.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        logger.LogInformation(
            "Quick search executed: {TermCount} term(s), {ResultCount} result(s).",
            termCount, results.Count);
        return Ok(results);
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
        var userId = await GetUserId();
        if (note.ParentNoteId.HasValue && !await IsActiveNote(userId, note.ParentNoteId.Value))
        {
            logger.LogWarning("Create rejected — parent note {ParentNoteId} not found, archived, or in recycle bin.", note.ParentNoteId);
            return BadRequest("Parent note does not exist, is archived, or is in the recycle bin.");
        }
        var created = await noteService.Create(userId, note);
        logger.LogInformation("Note created: {NoteId}", created.Id);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Update(Guid id, [FromBody] NoteItem note)
    {
        var userId = await GetUserId();
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
            if (!await IsActiveNote(userId, note.ParentNoteId.Value))
            {
                logger.LogWarning("Update rejected — parent note {ParentNoteId} not found, archived, or in recycle bin.", note.ParentNoteId);
                return BadRequest("Parent note does not exist, is archived, or is in the recycle bin.");
            }
        }

        var (updated, conflict, archived) = await noteService.Update(userId, id, note);
        if (archived)
            return Problem(
                detail: "Note is archived and read-only.",
                statusCode: StatusCodes.Status403Forbidden);
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
        logger.LogInformation("Note moved to recycle bin: {NoteId}", id);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        if (!await noteService.Archive(await GetUserId(), id))
        {
            logger.LogWarning("Archive failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note archived: {NoteId}", id);
        return NoContent();
    }

    [HttpPost("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id)
    {
        if (!await noteService.Unarchive(await GetUserId(), id))
        {
            logger.LogWarning("Unarchive failed — note {NoteId} not found or not archived", id);
            return NotFound();
        }
        logger.LogInformation("Note unarchived: {NoteId}", id);
        return NoContent();
    }

    [HttpGet("archive")]
    public async Task<ActionResult<PagedResult<ArchivedNoteSummary>>> GetArchive(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return Ok(await noteService.GetArchive(await GetUserId(), page, pageSize));
    }

    [HttpGet("recycle-bin")]
    public async Task<ActionResult<PagedResult<RecycleBinNoteSummary>>> GetRecycleBin(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return Ok(await noteService.GetRecycleBin(await GetUserId(), page, pageSize));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        if (!await noteService.Restore(await GetUserId(), id))
        {
            logger.LogWarning("Restore failed — note {NoteId} not found in recycle bin", id);
            return NotFound();
        }
        logger.LogInformation("Note restored from recycle bin: {NoteId}", id);
        return NoContent();
    }

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> DeletePermanent(Guid id)
    {
        var (found, wasInRecycleBin) = await noteService.DeletePermanent(await GetUserId(), id);
        if (!found)
        {
            logger.LogWarning("Permanent delete failed — note {NoteId} not found", id);
            return NotFound();
        }
        if (!wasInRecycleBin)
        {
            logger.LogWarning("Permanent delete rejected — note {NoteId} is not in recycle bin", id);
            return Conflict(new { message = "Note is not in the recycle bin. Move it to the recycle bin before deleting permanently." });
        }
        logger.LogInformation("Note permanently deleted: {NoteId}", id);
        return NoContent();
    }

    [HttpDelete("recycle-bin")]
    public async Task<IActionResult> EmptyRecycleBin()
    {
        var count = await noteService.EmptyRecycleBin(await GetUserId());
        logger.LogInformation("Recycle Bin emptied: {Count} note(s) permanently deleted", count);
        return NoContent();
    }

    /// <summary>Active = not soft-deleted (global filter) and not archived.
    /// Archived parents are rejected: no active note may live under an archived one.</summary>
    private async Task<bool> IsActiveNote(Guid userId, Guid noteId)
        => await db.Notes.AnyAsync(n => n.Id == noteId && n.UserId == userId && n.ArchivedAt == null);
}
