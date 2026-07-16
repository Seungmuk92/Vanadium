using System.ComponentModel.DataAnnotations;
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
    [HttpGet]
    public async Task<ActionResult<PagedResult<NoteSummary>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery][MaxLength(200)] string? search = null,
        [FromQuery] string sortBy = "date",
        [FromQuery] string sortDir = "desc",
        [FromQuery] Guid[]? labelIds = null,
        [FromQuery] bool includeLabels = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        if (labelIds is { Length: > 50 })
            return Problem(detail: "Too many label IDs (maximum 50).", statusCode: StatusCodes.Status400BadRequest);
        var result = await noteService.GetPaged(page, pageSize, search, sortBy, sortDir, labelIds, ct);
        if (includeLabels)
            result.Labels = await labelService.GetAllLabelsAsync();
        return Ok(result);
    }

    [HttpGet("mention-search")]
    public async Task<ActionResult<List<MentionSuggestionDto>>> MentionSearch(
        [FromQuery][MaxLength(100)] string q = "",
        CancellationToken ct = default)
    {
        return Ok(await noteService.SearchForMention(q, ct: ct));
    }

    [HttpGet("quick-search")]
    public async Task<ActionResult<List<QuickNavResult>>> QuickSearch(
        [FromQuery][MaxLength(200)] string q = "",
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await noteService.QuickSearch(q, limit, ct);
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
        [FromQuery] Guid[]? labelIds = null,
        CancellationToken ct = default)
    {
        if (labelIds is { Length: > 50 })
            return Problem(detail: "Too many label IDs (maximum 50).", statusCode: StatusCodes.Status400BadRequest);
        return Ok(await noteService.GetAllSummaries(labelIds, ct));
    }

    [HttpGet("{id:guid}/children")]
    public async Task<ActionResult<List<NoteSummary>>> GetChildren(Guid id, CancellationToken ct)
    {
        return Ok(await noteService.GetChildren(id, ct));
    }

    [HttpGet("{id:guid}/backlinks")]
    public async Task<ActionResult<List<BacklinkResult>>> GetBacklinks(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        return Ok(await noteService.GetBacklinks(id, limit, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Get(Guid id, CancellationToken ct)
    {
        var note = await noteService.Get(id, ct);
        if (note is null)
        {
            logger.LogWarning("Note {NoteId} not found", id);
            return NotFound();
        }
        return Ok(note);
    }

    [HttpPost]
    public async Task<ActionResult<NoteItem>> Create([FromBody] NoteItem note, CancellationToken ct)
    {
        if (note.ParentNoteId.HasValue && !await IsActiveNote(note.ParentNoteId.Value, ct))
        {
            logger.LogWarning("Create rejected — parent note {ParentNoteId} not found, archived, or in recycle bin.", note.ParentNoteId);
            return Problem(detail: "Parent note does not exist, is archived, or is in the recycle bin.", statusCode: StatusCodes.Status400BadRequest);
        }
        var created = await noteService.Create(note, ct);
        logger.LogInformation("Note created: {NoteId}", created.Id);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Update(Guid id, [FromBody] NoteItem note, [FromQuery] bool force, CancellationToken ct)
    {
        if (note.ParentNoteId.HasValue)
        {
            if (note.ParentNoteId.Value == id)
            {
                logger.LogWarning("Update rejected — note {NoteId} cannot be its own parent.", id);
                return Problem(detail: "A note cannot be its own parent.", statusCode: StatusCodes.Status400BadRequest);
            }
            if (await noteService.HasCircularReference(id, note.ParentNoteId.Value, ct))
            {
                logger.LogWarning("Update rejected — circular parent reference detected for note {NoteId}.", id);
                return Problem(detail: "Setting this parent would create a circular reference.", statusCode: StatusCodes.Status400BadRequest);
            }
            if (!await IsActiveNote(note.ParentNoteId.Value, ct))
            {
                logger.LogWarning("Update rejected — parent note {ParentNoteId} not found, archived, or in recycle bin.", note.ParentNoteId);
                return Problem(detail: "Parent note does not exist, is archived, or is in the recycle bin.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // force=true is an explicit, opt-in force-save that bypasses the optimistic
        // concurrency check. Without it, a client can no longer force-overwrite a
        // concurrent edit merely by sending a default/zero version (#221).
        var (updated, conflict, archived) = await noteService.Update(id, note, force, ct);
        if (archived)
            return Problem(
                detail: "Note is archived and read-only.",
                statusCode: StatusCodes.Status403Forbidden);
        if (conflict)
            return Problem(
                detail: "The note was modified by another session. Reload to get the latest version.",
                statusCode: StatusCodes.Status409Conflict);
        if (updated is null)
        {
            logger.LogWarning("Update failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note updated: {NoteId}", id);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!await noteService.Delete(id, ct))
        {
            logger.LogWarning("Delete failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note moved to recycle bin: {NoteId}", id);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        if (!await noteService.Archive(id, ct))
        {
            logger.LogWarning("Archive failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note archived: {NoteId}", id);
        return NoContent();
    }

    [HttpPost("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        if (!await noteService.Unarchive(id, ct))
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
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return Ok(await noteService.GetArchive(page, pageSize, ct));
    }

    [HttpGet("recycle-bin")]
    public async Task<ActionResult<PagedResult<RecycleBinNoteSummary>>> GetRecycleBin(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return Ok(await noteService.GetRecycleBin(page, pageSize, ct));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        if (!await noteService.Restore(id, ct))
        {
            logger.LogWarning("Restore failed — note {NoteId} not found in recycle bin", id);
            return NotFound();
        }
        logger.LogInformation("Note restored from recycle bin: {NoteId}", id);
        return NoContent();
    }

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> DeletePermanent(Guid id, CancellationToken ct)
    {
        var (found, wasInRecycleBin) = await noteService.DeletePermanent(id, ct);
        if (!found)
        {
            logger.LogWarning("Permanent delete failed — note {NoteId} not found", id);
            return NotFound();
        }
        if (!wasInRecycleBin)
        {
            logger.LogWarning("Permanent delete rejected — note {NoteId} is not in recycle bin", id);
            return Problem(
                detail: "Note is not in the recycle bin. Move it to the recycle bin before deleting permanently.",
                statusCode: StatusCodes.Status409Conflict);
        }
        logger.LogInformation("Note permanently deleted: {NoteId}", id);
        return NoContent();
    }

    [HttpDelete("recycle-bin")]
    public async Task<IActionResult> EmptyRecycleBin(CancellationToken ct)
    {
        var count = await noteService.EmptyRecycleBin(ct);
        logger.LogInformation("Recycle Bin emptied: {Count} note(s) permanently deleted", count);
        return NoContent();
    }

    [HttpGet("{id:guid}/share")]
    public async Task<ActionResult<ShareInfo>> GetShare(Guid id, CancellationToken ct)
    {
        var info = await noteService.GetShareInfo(id, ct);
        if (info is null)
        {
            logger.LogWarning("Get share failed — note {NoteId} not found", id);
            return NotFound();
        }
        return Ok(info);
    }

    [HttpPut("{id:guid}/share")]
    public async Task<ActionResult<ShareInfo>> SetShare(Guid id, [FromBody] SetShareRequest request, CancellationToken ct)
    {
        var info = await noteService.SetShare(id, request.Mode, ct);
        if (info is null)
        {
            logger.LogWarning("Set share failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note {NoteId} share mode set to {ShareMode}", id, request.Mode);
        return Ok(info);
    }

    [HttpDelete("{id:guid}/share")]
    public async Task<IActionResult> Unshare(Guid id, CancellationToken ct)
    {
        if (!await noteService.Unshare(id, ct))
        {
            logger.LogWarning("Unshare failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note {NoteId} unshared", id);
        return NoContent();
    }

    /// <summary>Active = not soft-deleted (global filter) and not archived.
    /// Archived parents are rejected: no active note may live under an archived one.</summary>
    private async Task<bool> IsActiveNote(Guid noteId, CancellationToken ct)
        => await db.Notes.AnyAsync(n => n.Id == noteId && n.ArchivedAt == null, ct);
}
