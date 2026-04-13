using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotesController(NoteService noteService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NoteItem>>> GetAll() =>
        Ok(await noteService.GetAll());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Get(Guid id)
    {
        var note = await noteService.Get(id);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPost]
    public async Task<ActionResult<NoteItem>> Create([FromBody] NoteItem note)
    {
        var created = await noteService.Create(note);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteItem>> Update(Guid id, [FromBody] NoteItem note)
    {
        var updated = await noteService.Update(id, note);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await noteService.Delete(id))
            return NotFound();

        return NoContent();
    }
}
