using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotesController(NoteService noteService) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<NoteItem>> GetAll() =>
        Ok(noteService.GetAll());

    [HttpGet("{id:guid}")]
    public ActionResult<NoteItem> Get(Guid id)
    {
        var note = noteService.Get(id);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPost]
    public ActionResult<NoteItem> Create([FromBody] NoteItem note)
    {
        var created = noteService.Create(note);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public ActionResult<NoteItem> Update(Guid id, [FromBody] NoteItem note)
    {
        var updated = noteService.Update(id, note);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        if (!noteService.Delete(id))
            return NotFound();

        return NoContent();
    }
}
