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
public class FilesController(IWebHostEnvironment env, NoteDbContext db, ILogger<FilesController> logger) : ControllerBase
{
    private string UploadsPath => Path.Combine(env.ContentRootPath, "uploads");

    private static readonly HashSet<string> AllowedContentTypes =
    [
        "application/pdf",
        "application/zip",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/markdown",
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
    ];

    [Authorize]
    [HttpPost]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult> Upload([FromForm] FileUploadRequest request)
    {
        if (request.File is null || request.File.Length == 0)
            return BadRequest("No file provided.");

        if (!AllowedContentTypes.Contains(request.File.ContentType))
        {
            logger.LogWarning("File upload rejected: unsupported content type '{ContentType}'", request.File.ContentType);
            return BadRequest($"File type '{request.File.ContentType}' is not allowed.");
        }

        var originalName = Path.GetFileName(request.File.FileName);
        logger.LogInformation("File upload requested: '{FileName}' ({Size} bytes, {ContentType})",
            originalName, request.File.Length, request.File.ContentType);

        Directory.CreateDirectory(UploadsPath);

        var id = Guid.NewGuid();
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

        logger.LogInformation("File saved: {FileId} ('{FileName}')", id, originalName);
        return Ok(new { url = $"/api/files/{id}", filename = originalName });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        logger.LogDebug("File download requested: {FileId}", id);

        var attachment = await db.FileAttachments.FindAsync(id);
        if (attachment is null)
        {
            logger.LogWarning("File attachment record not found: {FileId}", id);
            return NotFound();
        }

        var path = Path.Combine(UploadsPath, $"file_{id}");
        if (!System.IO.File.Exists(path))
        {
            logger.LogWarning("Physical file missing for attachment {FileId}: {Path}", id, path);
            return NotFound();
        }

        return PhysicalFile(path, attachment.ContentType, attachment.OriginalName);
    }
}
