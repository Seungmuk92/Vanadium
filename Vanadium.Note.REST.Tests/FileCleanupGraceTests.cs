using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Tests for the upload grace window in <c>FileCleanupService.DeleteAllOrphansAsync</c>
/// (issue #107): a freshly uploaded file may not yet be referenced by any note
/// because the editor auto-save is debounced, so the periodic scan must skip
/// files within the grace window (default 60 min) instead of collecting them.
/// </summary>
public class FileCleanupGraceTests
{
    [Fact]
    public async Task OrphanScan_RecentlyUploadedAttachment_IsSkipped()
    {
        using var h = new TestHost();

        // Uploaded just now, not yet referenced by any note (still within grace).
        var attachment = new FileAttachment
        {
            OriginalName = "in-progress.txt",
            ContentType = "text/plain",
            UploadedAt = DateTime.UtcNow,
        };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();
        var physicalPath = Path.Combine(h.ContentRoot, "uploads", $"file_{attachment.Id}");
        await File.WriteAllTextAsync(physicalPath, "draft body");

        await h.CreateNoteAsync("Unrelated", content: "<p>no references yet</p>");

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.True(File.Exists(physicalPath));
    }

    [Fact]
    public async Task OrphanScan_RecentlyCreatedImage_IsSkipped()
    {
        using var h = new TestHost();

        // Disk-only image (no DB record); a fresh file's creation time is within grace.
        var imageId = Guid.NewGuid();
        var imagePath = Path.Combine(h.ContentRoot, "uploads", $"{imageId}.png");
        await File.WriteAllTextAsync(imagePath, "png bytes");

        await h.CreateNoteAsync("Unrelated", content: "<p>no references yet</p>");

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.True(File.Exists(imagePath));
    }
}
