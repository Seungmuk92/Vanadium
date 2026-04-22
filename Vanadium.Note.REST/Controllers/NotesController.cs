using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotesController(NoteService noteService, LabelService labelService, ILogger<NotesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NoteSummary>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "date",
        [FromQuery] string sortDir = "desc",
        [FromQuery] Guid[]? labelIds = null,
        [FromQuery] bool includeLabels = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await noteService.GetPaged(page, pageSize, search, sortBy, sortDir, labelIds);
        if (includeLabels)
            result.Labels = await labelService.GetAllLabelsAsync();
        return Ok(result);
    }

    [HttpGet("summaries")]
    public async Task<ActionResult<List<NoteSummary>>> GetSummaries(
        [FromQuery] Guid[]? labelIds = null)
    {
        return Ok(await noteService.GetAllSummaries(labelIds));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Get(Guid id)
    {
        var note = await noteService.Get(id);
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
        var created = await noteService.Create(note);
        logger.LogInformation("Note created: {NoteId}", created.Id);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Update(Guid id, [FromBody] NoteItem note)
    {
        var updated = await noteService.Update(id, note);
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
        if (!await noteService.Delete(id))
        {
            logger.LogWarning("Delete failed — note {NoteId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Note deleted: {NoteId}", id);
        return NoContent();
    }
}
