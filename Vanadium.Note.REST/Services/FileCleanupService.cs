using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Deletes uploaded files (attachments and images) that are no longer
/// referenced by any note. Used both at note-deletion time and by the
/// periodic background GC job.
/// </summary>
public class FileCleanupService(
    NoteDbContext db,
    IWebHostEnvironment env,
    IConfiguration config,
    ILogger<FileCleanupService> logger)
{
    private const int DefaultGraceMinutes = 60;

    private string UploadsPath => Path.Combine(env.ContentRootPath, "uploads");

    // Matches /api/files/{guid} — file attachments stored in DB
    private static readonly Regex FilePattern =
        new(@"/api/files/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches /api/images/{guid} — images stored only on disk
    private static readonly Regex ImagePattern =
        new(@"/api/images/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static HashSet<Guid> ExtractIds(string html, Regex pattern) =>
        pattern.Matches(html)
               .Select(m => Guid.Parse(m.Groups[1].Value))
               .ToHashSet();

    /// <summary>
    /// Called immediately after a note is deleted.
    /// Deletes files/images that were referenced in the note's content
    /// and are no longer referenced by any remaining note.
    /// </summary>
    public async Task DeleteOrphanedFromContentAsync(string deletedContent, CancellationToken ct = default)
    {
        var fileIds  = ExtractIds(deletedContent, FilePattern);
        var imageIds = ExtractIds(deletedContent, ImagePattern);

        if (fileIds.Count == 0 && imageIds.Count == 0)
            return;

        logger.LogDebug("On-delete orphan check: {FileCount} file(s) and {ImageCount} image(s) referenced in deleted content.",
            fileIds.Count, imageIds.Count);

        // Combine all surviving notes' content into one string for fast lookup.
        // IgnoreQueryFilters: soft-deleted notes still reference their files — their
        // attachments must NOT be treated as orphans until they are purged.
        var survivingContent = string.Join(' ',
            await db.Notes.IgnoreQueryFilters().Select(n => n.Content).ToListAsync(ct));

        var filesRemoved  = await DeleteFileAttachmentsAsync(fileIds, survivingContent, ct);
        var imagesRemoved = DeleteImageFiles(imageIds, survivingContent);

        if (filesRemoved > 0 || imagesRemoved > 0)
            logger.LogInformation(
                "On-delete cleanup complete — {Files} file attachment(s) and {Images} image(s) removed.",
                filesRemoved, imagesRemoved);
    }

    /// <summary>
    /// Full orphan scan: called by the periodic background job.
    /// Removes every file/image not referenced in any note.
    /// </summary>
    public async Task DeleteAllOrphansAsync(CancellationToken ct = default)
    {
        // Files uploaded within the grace window are skipped: a freshly uploaded
        // file may not yet be referenced in any note's HTML because the editor
        // auto-save is debounced (~1500ms) and the note may still be in progress.
        var graceMinutes = config.GetValue("FileCleanup:GraceMinutes", DefaultGraceMinutes);
        var graceCutoff = DateTime.UtcNow.AddMinutes(-graceMinutes);

        // IgnoreQueryFilters: content of soft-deleted notes still counts as a live
        // file reference until the recycle bin purge actually deletes the note.
        var allContent = string.Join(' ',
            await db.Notes.IgnoreQueryFilters().Select(n => n.Content).ToListAsync(ct));

        // --- File attachments (have DB records) ---
        var attachments = await db.FileAttachments.ToListAsync(ct);

        logger.LogDebug(
            "Periodic orphan scan: checking {AttachmentCount} file attachment record(s) (grace: {GraceMinutes}m).",
            attachments.Count, graceMinutes);

        int filesRemoved = 0;

        foreach (var attachment in attachments)
        {
            if (allContent.Contains(attachment.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip recently uploaded attachments still within the grace window.
            if (attachment.UploadedAt > graceCutoff)
            {
                logger.LogDebug(
                    "Periodic scan: skipping recently uploaded attachment {FileId} (within grace window).",
                    attachment.Id);
                continue;
            }

            db.FileAttachments.Remove(attachment);
            DeletePhysicalFile(Path.Combine(UploadsPath, $"file_{attachment.Id}"));
            logger.LogDebug("Periodic scan: removed orphaned file attachment {FileId} ('{FileName}').",
                attachment.Id, attachment.OriginalName);
            filesRemoved++;
        }

        if (filesRemoved > 0)
            await db.SaveChangesAsync(ct);

        // --- Images (disk-only, no DB record) ---
        if (!Directory.Exists(UploadsPath))
        {
            logger.LogInformation(
                "Orphan cleanup complete — {Files} file attachment(s) and 0 image(s) removed.", filesRemoved);
            return;
        }

        int imagesRemoved = 0;

        foreach (var file in new DirectoryInfo(UploadsPath).EnumerateFiles())
        {
            // file_ prefix = file attachment, already handled above
            if (file.Name.StartsWith("file_", StringComparison.OrdinalIgnoreCase))
                continue;

            var guidStr = Path.GetFileNameWithoutExtension(file.Name);
            if (!Guid.TryParse(guidStr, out _))
                continue;

            if (allContent.Contains(guidStr, StringComparison.OrdinalIgnoreCase))
                continue;

            // Disk-only images have no DB record, so fall back to the file's
            // creation time to honor the same grace window.
            if (file.CreationTimeUtc > graceCutoff)
            {
                logger.LogDebug(
                    "Periodic scan: skipping recently created image {ImageFile} (within grace window).",
                    file.Name);
                continue;
            }

            DeletePhysicalFile(file.FullName);
            logger.LogDebug("Periodic scan: removed orphaned image {ImageFile}.", file.Name);
            imagesRemoved++;
        }

        logger.LogInformation(
            "Orphan cleanup complete — {Files} file attachment(s) and {Images} image(s) removed.",
            filesRemoved, imagesRemoved);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<int> DeleteFileAttachmentsAsync(
        IEnumerable<Guid> ids, string survivingContent, CancellationToken ct)
    {
        int removed = 0;

        foreach (var id in ids)
        {
            if (survivingContent.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var attachment = await db.FileAttachments.FindAsync([id], ct);
            if (attachment is null) continue;

            db.FileAttachments.Remove(attachment);
            DeletePhysicalFile(Path.Combine(UploadsPath, $"file_{id}"));
            logger.LogDebug("On-delete: removed orphaned file attachment {FileId} ('{FileName}').",
                id, attachment.OriginalName);
            removed++;
        }

        if (removed > 0)
            await db.SaveChangesAsync(ct);

        return removed;
    }

    private int DeleteImageFiles(IEnumerable<Guid> ids, string survivingContent)
    {
        if (!Directory.Exists(UploadsPath)) return 0;

        int removed = 0;

        foreach (var id in ids)
        {
            if (survivingContent.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var file = new DirectoryInfo(UploadsPath)
                .EnumerateFiles($"{id}.*")
                .FirstOrDefault();

            if (file is null) continue;

            DeletePhysicalFile(file.FullName);
            logger.LogDebug("On-delete: removed orphaned image {ImageId}.", id);
            removed++;
        }

        return removed;
    }

    private void DeletePhysicalFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete physical file: {Path}", path);
        }
    }
}
