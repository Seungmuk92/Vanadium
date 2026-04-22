using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Vanadium.Note.REST.Controllers;

public class ImageUploadRequest
{
    public IFormFile File { get; set; } = null!;
}

[ApiController]
[Route("api/[controller]")]
public class ImagesController(IWebHostEnvironment env, ILogger<ImagesController> logger) : ControllerBase
{
    private string UploadsPath => Path.Combine(env.ContentRootPath, "uploads");

    [Authorize]
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult> Upload([FromForm] ImageUploadRequest request)
    {
        if (request.File is null || request.File.Length == 0)
            return BadRequest("No file provided.");

        var imageType = await DetectImageTypeAsync(request.File);
        if (imageType is null)
        {
            logger.LogWarning("Image upload rejected: unrecognized format '{FileName}' ({ContentType})",
                request.File.FileName, request.File.ContentType);
            return BadRequest("File is not a supported image (JPEG, PNG, GIF, WebP).");
        }

        logger.LogInformation("Image upload requested: '{FileName}' ({Size} bytes, {ContentType})",
            request.File.FileName, request.File.Length, imageType);

        Directory.CreateDirectory(UploadsPath);

        var id = Guid.NewGuid();
        var ext = ToExtension(imageType);
        var path = Path.Combine(UploadsPath, $"{id}{ext}");

        await using var stream = System.IO.File.Create(path);
        await request.File.CopyToAsync(stream);

        logger.LogInformation("Image saved: {ImageId}{Ext}", id, ext);
        return Ok(new { url = $"/api/images/{id}" });
    }

    [HttpGet("{id:guid}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public IActionResult Get(Guid id)
    {
        logger.LogDebug("Image requested: {ImageId}", id);
        var file = new DirectoryInfo(UploadsPath).GetFiles($"{id}.*").FirstOrDefault();
        if (file is null)
        {
            logger.LogWarning("Image not found: {ImageId}", id);
            return NotFound();
        }
        return PhysicalFile(file.FullName, ToContentType(file.Extension));
    }

    private static async Task<string?> DetectImageTypeAsync(IFormFile file)
    {
        var buffer = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer);
        if (read < 4) return null;

        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return "image/jpeg";
        if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
            return "image/png";
        if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x38)
            return "image/gif";
        if (read >= 12 &&
            buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
            buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
            return "image/webp";

        return null;
    }

    private static string ToExtension(string contentType) => contentType switch
    {
        "image/gif"  => ".gif",
        "image/png"  => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _            => ".bin",
    };

    private static string ToContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".gif"          => "image/gif",
        ".png"          => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp"         => "image/webp",
        _               => "application/octet-stream",
    };
}
