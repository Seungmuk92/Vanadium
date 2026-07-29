using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

/// <summary>
/// Note Properties (issue #343): definition CRUD, option CRUD, and per-note value upsert/clear.
/// Hosts both the definition/option routes and the note-value routes, mirroring how
/// <see cref="LabelsController"/> hosts label CRUD plus note-label assignment.
/// </summary>
[Authorize]
[ApiController]
public class PropertiesController(PropertyService properties, ILogger<PropertiesController> logger) : ControllerBase
{
    // ── Definitions ──────────────────────────────────────────────────────────────

    [HttpGet("api/properties")]
    public async Task<ActionResult<IEnumerable<PropertyDefinitionDto>>> GetAll(
        [FromQuery] bool includeUsage = false, CancellationToken ct = default) =>
        Ok(await properties.GetAllAsync(includeUsage, ct));

    [HttpPost("api/properties")]
    public async Task<ActionResult<PropertyDefinitionDto>> Create(
        [FromBody] CreatePropertyDefinitionRequest req, CancellationToken ct)
    {
        try
        {
            var result = await properties.CreateAsync(req.Name, req.Type, ct);
            return Created($"api/properties/{result.Id}", result);
        }
        catch (PropertyService.DuplicateNameException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex) when (ex is PropertyService.CapExceededException or PropertyService.PropertyValidationException)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPut("api/properties/{id:guid}")]
    public async Task<ActionResult<PropertyDefinitionDto>> Update(
        Guid id, [FromBody] UpdatePropertyDefinitionRequest req, CancellationToken ct)
    {
        try
        {
            var result = await properties.UpdateAsync(id, req.Name, req.Type, req.SortOrder, ct);
            if (result is null) return NotFound();
            return Ok(result);
        }
        catch (Exception ex) when (ex is PropertyService.DuplicateNameException or PropertyService.TypeChangeBlockedException)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (PropertyService.PropertyValidationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpDelete("api/properties/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!await properties.DeleteAsync(id, ct)) return NotFound();
        return NoContent();
    }

    // ── Options ──────────────────────────────────────────────────────────────────

    [HttpPost("api/properties/{id:guid}/options")]
    public async Task<ActionResult<PropertyOptionDto>> AddOption(
        Guid id, [FromBody] CreatePropertyOptionRequest req, CancellationToken ct)
    {
        try
        {
            var result = await properties.AddOptionAsync(id, req.Name, ct);
            if (result is null) return NotFound();
            return Created($"api/properties/{id}/options/{result.Id}", result);
        }
        catch (PropertyService.DuplicateNameException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex) when (ex is PropertyService.CapExceededException or PropertyService.PropertyValidationException)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPut("api/properties/{id:guid}/options/{optionId:guid}")]
    public async Task<ActionResult<PropertyOptionDto>> UpdateOption(
        Guid id, Guid optionId, [FromBody] UpdatePropertyOptionRequest req, CancellationToken ct)
    {
        try
        {
            var result = await properties.UpdateOptionAsync(id, optionId, req.Name, req.SortOrder, ct);
            if (result is null) return NotFound();
            return Ok(result);
        }
        catch (PropertyService.DuplicateNameException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (PropertyService.PropertyValidationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpDelete("api/properties/{id:guid}/options/{optionId:guid}")]
    public async Task<IActionResult> DeleteOption(Guid id, Guid optionId, CancellationToken ct)
    {
        if (!await properties.DeleteOptionAsync(id, optionId, ct)) return NotFound();
        return NoContent();
    }

    // ── Note values ────────────────────────────────────────────────────────────────

    [HttpPut("api/notes/{noteId:guid}/properties/{definitionId:guid}")]
    public async Task<ActionResult<NotePropertyValueDto>> SetValue(
        Guid noteId, Guid definitionId, [FromBody] SetNotePropertyValueRequest req, CancellationToken ct)
    {
        try
        {
            var result = await properties.SetValueAsync(noteId, definitionId, req, ct);
            if (result is null) return NotFound();
            return Ok(result);
        }
        catch (PropertyService.NoteArchivedException)
        {
            logger.LogWarning("SetValue rejected — note {NoteId} is archived and read-only.", noteId);
            return Problem(detail: "Note is archived and read-only.", statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PropertyService.PropertyValidationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpDelete("api/notes/{noteId:guid}/properties/{definitionId:guid}")]
    public async Task<IActionResult> ClearValue(Guid noteId, Guid definitionId, CancellationToken ct)
    {
        try
        {
            if (!await properties.ClearValueAsync(noteId, definitionId, ct)) return NotFound();
            return NoContent();
        }
        catch (PropertyService.NoteArchivedException)
        {
            logger.LogWarning("ClearValue rejected — note {NoteId} is archived and read-only.", noteId);
            return Problem(detail: "Note is archived and read-only.", statusCode: StatusCodes.Status403Forbidden);
        }
    }
}
