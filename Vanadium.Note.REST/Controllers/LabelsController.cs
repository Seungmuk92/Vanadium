using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
public class LabelsController(LabelService labelService, ILogger<LabelsController> logger) : ControllerBase
{
    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet("api/label-categories")]
    public async Task<ActionResult<IEnumerable<LabelCategoryDto>>> GetCategories() =>
        Ok(await labelService.GetAllCategoriesAsync());

    [HttpPost("api/label-categories")]
    public async Task<ActionResult<LabelCategoryDto>> CreateCategory([FromBody] NameRequest req)
    {
        try
        {
            var result = await labelService.CreateCategoryAsync(req.Name);
            logger.LogInformation("Label category created: '{Name}' ({Id})", result.Name, result.Id);
            return Created($"api/label-categories/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Duplicate label category: '{Name}'", req.Name);
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("api/label-categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        if (!await labelService.DeleteCategoryAsync(id))
        {
            logger.LogWarning("Delete failed — label category {CategoryId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Label category deleted: {CategoryId}", id);
        return NoContent();
    }

    // ── Labels ────────────────────────────────────────────────────────────────

    [HttpGet("api/labels")]
    public async Task<ActionResult<IEnumerable<LabelSummary>>> GetLabels() =>
        Ok(await labelService.GetAllLabelsAsync());

    [HttpPost("api/labels")]
    public async Task<ActionResult<LabelSummary>> CreateLabel([FromBody] CreateLabelRequest req)
    {
        try
        {
            var result = await labelService.CreateLabelAsync(req.Name, req.CategoryId);
            logger.LogInformation("Label created: '{Name}' ({Id})", result.Name, result.Id);
            return Created($"api/labels/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Duplicate label: '{Name}'", req.Name);
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("api/labels/{id:guid}")]
    public async Task<IActionResult> DeleteLabel(Guid id)
    {
        if (!await labelService.DeleteLabelAsync(id))
        {
            logger.LogWarning("Delete failed — label {LabelId} not found", id);
            return NotFound();
        }
        logger.LogInformation("Label deleted: {LabelId}", id);
        return NoContent();
    }

    // ── Note-Label assignments ────────────────────────────────────────────────

    [HttpPost("api/notes/{noteId:guid}/labels")]
    public async Task<IActionResult> AddLabel(Guid noteId, [FromBody] AddLabelRequest req)
    {
        try
        {
            await labelService.AddLabelToNoteAsync(noteId, req.LabelId);
            logger.LogInformation("Label {LabelId} added to note {NoteId}", req.LabelId, noteId);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            logger.LogWarning("AddLabel failed — label {LabelId} not found", req.LabelId);
            return NotFound();
        }
    }

    [HttpDelete("api/notes/{noteId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> RemoveLabel(Guid noteId, Guid labelId)
    {
        if (!await labelService.RemoveLabelFromNoteAsync(noteId, labelId))
        {
            logger.LogWarning("RemoveLabel failed — assignment not found (note {NoteId}, label {LabelId})", noteId, labelId);
            return NotFound();
        }
        logger.LogInformation("Label {LabelId} removed from note {NoteId}", labelId, noteId);
        return NoContent();
    }
}

public record NameRequest(string Name);
public record CreateLabelRequest(string Name, Guid? CategoryId);
public record AddLabelRequest(Guid LabelId);
