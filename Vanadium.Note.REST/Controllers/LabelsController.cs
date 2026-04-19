using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
public class LabelsController(LabelService labelService) : ControllerBase
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
            return Created($"api/label-categories/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("api/label-categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id) =>
        await labelService.DeleteCategoryAsync(id) ? NoContent() : NotFound();

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
            return Created($"api/labels/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("api/labels/{id:guid}")]
    public async Task<IActionResult> DeleteLabel(Guid id) =>
        await labelService.DeleteLabelAsync(id) ? NoContent() : NotFound();

    // ── Note-Label assignments ────────────────────────────────────────────────

    [HttpPost("api/notes/{noteId:guid}/labels")]
    public async Task<IActionResult> AddLabel(Guid noteId, [FromBody] AddLabelRequest req)
    {
        try
        {
            await labelService.AddLabelToNoteAsync(noteId, req.LabelId);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("api/notes/{noteId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> RemoveLabel(Guid noteId, Guid labelId) =>
        await labelService.RemoveLabelFromNoteAsync(noteId, labelId) ? NoContent() : NotFound();
}

public record NameRequest(string Name);
public record CreateLabelRequest(string Name, Guid? CategoryId);
public record AddLabelRequest(Guid LabelId);
