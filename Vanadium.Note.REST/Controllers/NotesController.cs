using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotesController(NoteService noteService) : ControllerBase
{
    [HttpPost]
    public ActionResult<NoteItem> Create([FromBody] NoteItem note)
    {
        var created = noteService.Create(note);
        return CreatedAtAction(nameof(Create), new { id = created.Id }, created);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        if (!noteService.Delete(id))
            return NotFound();

        return NoContent();
    }
}
