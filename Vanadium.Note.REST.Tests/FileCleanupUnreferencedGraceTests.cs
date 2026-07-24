using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Tests for the first-observed-unreferenced grace window in
/// <c>FileCleanupService.DeleteAllOrphansAsync</c> (issue #301): the grace window is
/// measured from the first scan that saw an asset unreferenced, NOT from its upload/creation
/// time. A draft asset that stays unsaved past the upload grace must therefore survive the
/// scan that first observes it, and is only collected once it has stayed unreferenced for the
/// whole grace window across scans.
/// </summary>
public class FileCleanupUnreferencedGraceTests
{
    // Pushes the tracker's "first observed unreferenced" instant for an asset far enough into
    // the past that the next scan considers the grace window (default 60 min) elapsed.
    private static void SeedUnreferencedLongAgo(TestHost h, Guid id) =>
        h.OrphanTracker.ObserveUnreferenced(id, DateTime.UtcNow.AddDays(-1));

    [Fact]
    public async Task OrphanScan_AttachmentUploadedLongAgoButNeverScanned_SurvivesFirstScan()
    {
        using var h = new TestHost();

        // Uploaded two hours ago (well past the 60-min upload grace) but never scanned before,
        // e.g. a draft whose auto-save has been failing. Under the old upload-time grace this
        // would be deleted; now the first scan only records it and must skip.
        var attachment = new FileAttachment
        {
            OriginalName = "stuck-draft.txt",
            ContentType = "text/plain",
            UploadedAt = DateTime.UtcNow.AddHours(-2),
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
    public async Task OrphanScan_ImageCreatedLongAgoButNeverScanned_SurvivesFirstScan()
    {
        using var h = new TestHost();

        var imageId = Guid.NewGuid();
        var imagePath = Path.Combine(h.ContentRoot, "uploads", $"{imageId}.png");
        await File.WriteAllTextAsync(imagePath, "png bytes");
        File.SetCreationTimeUtc(imagePath, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(imagePath, DateTime.UtcNow.AddHours(-2));

        await h.CreateNoteAsync("Unrelated", content: "<p>no references yet</p>");

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.True(File.Exists(imagePath));
    }

    [Fact]
    public async Task OrphanScan_AttachmentUnreferencedPastGrace_IsCollected()
    {
        using var h = new TestHost();

        var attachment = new FileAttachment
        {
            OriginalName = "truly-orphaned.txt",
            ContentType = "text/plain",
            UploadedAt = DateTime.UtcNow.AddHours(-2),
        };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();
        var physicalPath = Path.Combine(h.ContentRoot, "uploads", $"file_{attachment.Id}");
        await File.WriteAllTextAsync(physicalPath, "orphan body");

        await h.CreateNoteAsync("Unrelated", content: "<p>no references</p>");

        // Simulate an earlier scan that already observed it unreferenced beyond the grace window.
        SeedUnreferencedLongAgo(h, attachment.Id);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.Null(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.False(File.Exists(physicalPath));
    }

    [Fact]
    public async Task OrphanScan_ImageUnreferencedPastGrace_IsCollected()
    {
        using var h = new TestHost();

        var imageId = Guid.NewGuid();
        var imagePath = Path.Combine(h.ContentRoot, "uploads", $"{imageId}.png");
        await File.WriteAllTextAsync(imagePath, "png bytes");

        await h.CreateNoteAsync("Unrelated", content: "<p>no references</p>");

        SeedUnreferencedLongAgo(h, imageId);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.False(File.Exists(imagePath));
    }

    [Fact]
    public async Task OrphanScan_AttachmentReferencedAgainBeforeGrace_ResetsClockAndSurvives()
    {
        using var h = new TestHost();

        var attachment = new FileAttachment
        {
            OriginalName = "rescued.txt",
            ContentType = "text/plain",
            UploadedAt = DateTime.UtcNow.AddHours(-2),
        };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();
        var physicalPath = Path.Combine(h.ContentRoot, "uploads", $"file_{attachment.Id}");
        await File.WriteAllTextAsync(physicalPath, "rescued body");

        // The draft finally saved: a note now references the attachment.
        await h.CreateNoteAsync("Saved",
            content: $"<p><a href=\"/api/files/{attachment.Id}\">doc</a></p>");

        // Even though a prior scan had marked it unreferenced past grace, it is referenced now.
        SeedUnreferencedLongAgo(h, attachment.Id);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.True(File.Exists(physicalPath));

        // The reset means a later unreferenced spell must accumulate grace afresh: a scan
        // that observes it unreferenced for the first time again must NOT delete it.
        await h.Db.Notes.ExecuteDeleteAsync();
        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.True(File.Exists(physicalPath));
    }
}
