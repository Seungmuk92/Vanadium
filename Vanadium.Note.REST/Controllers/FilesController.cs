using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Controllers;

public class FileUploadRequest
{
    public IFormFile File { get; set; } = null!;
}

[ApiController]
[Route("api/[controller]")]
public class FilesController(IWebHostEnvironment env, NoteDbContext db) : ControllerBase
{
    private string UploadsPath => Path.Combine(env.ContentRootPath, "uploads");

    [Authorize]
    [HttpPost]
    [DisableRequestSizeLimit]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult> Upload([FromForm] FileUploadRequest request)
    {
        Directory.CreateDirectory(UploadsPath);

        var id = Guid.NewGuid();
        var originalName = Path.GetFileName(request.File.FileName);
        var path = Path.Combine(UploadsPath, $"file_{id}");

        await using var stream = System.IO.File.Create(path);
        await request.File.CopyToAsync(stream);

        var attachment = new FileAttachment
        {
            Id = id,
            OriginalName = originalName,
            ContentType = string.IsNullOrWhiteSpace(request.File.ContentType)
                ? "application/octet-stream"
                : request.File.ContentType,
        };
        db.FileAttachments.Add(attachment);
        await db.SaveChangesAsync();

        return Ok(new { url = $"/api/files/{id}", filename = originalName });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var attachment = await db.FileAttachments.FindAsync(id);
        if (attachment is null) return NotFound();

        var path = Path.Combine(UploadsPath, $"file_{id}");
        if (!System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, attachment.ContentType, attachment.OriginalName);
    }
}
