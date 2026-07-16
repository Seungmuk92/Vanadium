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
            return Problem(detail: "No file provided.", statusCode: StatusCodes.Status400BadRequest);

        if (!AllowedContentTypes.Contains(request.File.ContentType))
        {
            logger.LogWarning("File upload rejected: unsupported content type '{ContentType}'", request.File.ContentType);
            return Problem(detail: $"File type '{request.File.ContentType}' is not allowed.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await IsContentValidAsync(request.File))
        {
            logger.LogWarning("File upload rejected: content validation failed for '{ContentType}' ('{FileName}')",
                request.File.ContentType, request.File.FileName);
            return Problem(detail: "File content does not match the declared file type.", statusCode: StatusCodes.Status400BadRequest);
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
            ContentType = request.File.ContentType,
        };
        db.FileAttachments.Add(attachment);
        await db.SaveChangesAsync();

        logger.LogInformation("File saved: {FileId} ('{FileName}')", id, originalName);
        return Ok(new { url = $"/api/files/{id}", filename = originalName });
    }

    private static readonly HashSet<string> TextContentTypes = ["text/plain", "text/markdown"];

    // Number of leading bytes sampled for the text/binary heuristic. A prefix is
    // enough to catch obvious binary payloads without buffering the whole upload.
    private const int TextSampleSize = 8192;

    // Reject if more than this fraction of sampled bytes are suspicious control
    // characters. Real text/markdown stays well under this; binary blobs blow past it.
    private const double MaxControlCharRatio = 0.10;

    private static Task<bool> IsContentValidAsync(IFormFile file) =>
        TextContentTypes.Contains(file.ContentType)
            ? IsLikelyTextAsync(file)
            : HasValidMagicBytesAsync(file, file.ContentType);

    // text/plain and text/markdown have no reliable magic bytes, so instead of
    // trusting the client Content-Type we sniff a prefix and reject obvious binary
    // (NUL bytes or a high ratio of non-whitespace control characters).
    private static async Task<bool> IsLikelyTextAsync(IFormFile file)
    {
        var buffer = new byte[TextSampleSize];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer);
        if (read == 0) return false;

        return IsLikelyText(buffer.AsSpan(0, read));
    }

    internal static bool IsLikelyText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;

        var suspicious = 0;
        foreach (var b in bytes)
        {
            // A NUL byte is a strong binary signal (UTF-8/ASCII text never contains one).
            if (b == 0x00) return false;

            // Allow common text whitespace/control chars: tab, LF, VT, FF, CR.
            var isAllowedWhitespace = b is 0x09 or 0x0A or 0x0B or 0x0C or 0x0D;
            if ((b < 0x20 && !isAllowedWhitespace) || b == 0x7F)
                suspicious++;
        }

        // High bytes (0x80–0xFF) are left uncounted so legitimate UTF-8 / Latin-1
        // text passes; only control-character density drives the decision.
        return suspicious <= bytes.Length * MaxControlCharRatio;
    }

    private static async Task<bool> HasValidMagicBytesAsync(IFormFile file, string contentType)
    {
        var buffer = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer);

        return HasValidMagicBytes(buffer, read, contentType);
    }

    internal static bool HasValidMagicBytes(ReadOnlySpan<byte> buffer, int read, string contentType)
    {
        if (read < 4) return false;

        return contentType switch
        {
            // %PDF
            "application/pdf" =>
                buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46,

            // PK — ZIP-based formats (zip / docx / xlsx are indistinguishable at magic bytes level)
            "application/zip" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04,

            // OLE Compound Document — legacy .doc / .xls
            "application/msword" or
            "application/vnd.ms-excel" =>
                buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0,

            // Images
            "image/jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,
            "image/png"  => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,
            "image/gif"  => buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x38,

            // RIFF container (offset 0) + WEBP signature (offset 8). Matching only the
            // RIFF prefix would also accept AVI/WAV, so the offset-8 check is required —
            // this mirrors ImagesController.DetectImageTypeAsync.
            "image/webp" => read >= 12 &&
                            buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                            buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50,

            // text/plain and text/markdown are validated separately by IsLikelyTextAsync.
            _ => false
        };
    }

    [Authorize]
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
