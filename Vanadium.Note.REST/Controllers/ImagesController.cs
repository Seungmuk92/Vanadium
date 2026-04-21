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
    [DisableRequestSizeLimit]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult> Upload([FromForm] ImageUploadRequest request)
    {
        logger.LogInformation("Image upload requested: '{FileName}' ({Size} bytes, {ContentType})",
            request.File.FileName, request.File.Length, request.File.ContentType);

        Directory.CreateDirectory(UploadsPath);

        var id = Guid.NewGuid();
        var ext = ToExtension(request.File.ContentType);
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
