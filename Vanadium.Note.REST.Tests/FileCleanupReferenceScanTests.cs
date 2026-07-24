using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression tests for issue #219: after moving the orphan-file reference scan onto an
/// indexed <c>Content</c> substring probe, the scan must still count references inside
/// soft-deleted (recycle-bin) and archived notes as live, so those notes' attachments are
/// never garbage-collected early. These exercise the SQLite fallback path of
/// <c>FileCleanupService.IsReferencedInAnyNoteAsync</c>; the PostgreSQL trigram <c>ILIKE</c>
/// path is verified manually (per repo convention), but the reference semantics under test
/// are provider-independent.
/// </summary>
public class FileCleanupReferenceScanTests
{
    // Older than the default 60-minute grace window, so an unreferenced attachment is eligible
    // for removal (referenced attachments are kept regardless of age).
    private static readonly TimeSpan BeyondGrace = TimeSpan.FromHours(2);

    private static async Task<FileAttachment> AddAttachmentAsync(TestHost h, DateTime uploadedAt)
    {
        var attachment = new FileAttachment
        {
            OriginalName = "spec.pdf",
            ContentType = "application/pdf",
            UploadedAt = uploadedAt,
        };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();
        await File.WriteAllTextAsync(PhysicalPath(h, attachment.Id), "body");
        return attachment;
    }

    /// <summary>Inserts a note with exact content, bypassing the service sanitizer so the
    /// reference markup under test is preserved verbatim.</summary>
    private static async Task AddNoteAsync(
        TestHost h, string content, DateTime? deletedAt = null, DateTime? archivedAt = null)
    {
        h.Db.Notes.Add(new NoteItem
        {
            Title = "Referencing",
            Content = content,
            ContentText = content,
            DeletedAt = deletedAt,
            ArchivedAt = archivedAt,
        });
        await h.Db.SaveChangesAsync();
    }

    private static string PhysicalPath(TestHost h, Guid id) =>
        Path.Combine(h.ContentRoot, "uploads", $"file_{id}");

    [Fact]
    public async Task OrphanScan_AttachmentReferencedBySoftDeletedNote_IsKept()
    {
        using var h = new TestHost();
        var attachment = await AddAttachmentAsync(h, DateTime.UtcNow - BeyondGrace);
        await AddNoteAsync(
            h, $"<p><a href=\"/api/files/{attachment.Id}\">spec.pdf</a></p>",
            deletedAt: DateTime.UtcNow);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.True(File.Exists(PhysicalPath(h, attachment.Id)));
    }

    [Fact]
    public async Task OrphanScan_AttachmentReferencedByArchivedNote_IsKept()
    {
        using var h = new TestHost();
        var attachment = await AddAttachmentAsync(h, DateTime.UtcNow - BeyondGrace);
        await AddNoteAsync(
            h, $"<p><a href=\"/api/files/{attachment.Id}\">spec.pdf</a></p>",
            archivedAt: DateTime.UtcNow);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.True(File.Exists(PhysicalPath(h, attachment.Id)));
    }

    [Fact]
    public async Task OrphanScan_UnreferencedAttachment_IsRemoved()
    {
        using var h = new TestHost();
        var attachment = await AddAttachmentAsync(h, DateTime.UtcNow - BeyondGrace);
        await AddNoteAsync(h, "<p>no file references here</p>");

        // Grace is measured from first-observed-unreferenced (issue #301): simulate an earlier
        // scan that already saw it unreferenced beyond the grace window so this scan collects it.
        h.OrphanTracker.ObserveUnreferenced(attachment.Id, DateTime.UtcNow - BeyondGrace);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.Null(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.False(File.Exists(PhysicalPath(h, attachment.Id)));
    }
}
