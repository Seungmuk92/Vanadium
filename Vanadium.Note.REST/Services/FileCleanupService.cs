using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Deletes uploaded files (attachments and images) that are no longer
/// referenced by any note. Used both at note-deletion time and by the
/// periodic background GC job.
/// </summary>
public class FileCleanupService(NoteDbContext db, IWebHostEnvironment env, ILogger<FileCleanupService> logger)
{
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

        // Combine all surviving notes' content into one string for fast lookup
        var survivingContent = string.Join(' ',
            await db.Notes.Select(n => n.Content).ToListAsync(ct));

        await DeleteFileAttachmentsAsync(fileIds, survivingContent, ct);
        DeleteImageFiles(imageIds, survivingContent);
    }

    /// <summary>
    /// Full orphan scan: called by the periodic background job.
    /// Removes every file/image not referenced in any note.
    /// </summary>
    public async Task DeleteAllOrphansAsync(CancellationToken ct = default)
    {
        var allContent = string.Join(' ',
            await db.Notes.Select(n => n.Content).ToListAsync(ct));

        // --- File attachments (have DB records) ---
        var attachments = await db.FileAttachments.ToListAsync(ct);
        int filesRemoved = 0;

        foreach (var attachment in attachments)
        {
            if (allContent.Contains(attachment.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            db.FileAttachments.Remove(attachment);
            DeletePhysicalFile(Path.Combine(UploadsPath, $"file_{attachment.Id}"));
            filesRemoved++;
        }

        if (filesRemoved > 0)
            await db.SaveChangesAsync(ct);

        // --- Images (disk-only, no DB record) ---
        if (!Directory.Exists(UploadsPath))
            return;

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

            DeletePhysicalFile(file.FullName);
            imagesRemoved++;
        }

        logger.LogInformation(
            "Orphan cleanup complete — {Files} file attachment(s) and {Images} image(s) removed.",
            filesRemoved, imagesRemoved);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task DeleteFileAttachmentsAsync(
        IEnumerable<Guid> ids, string survivingContent, CancellationToken ct)
    {
        bool changed = false;

        foreach (var id in ids)
        {
            if (survivingContent.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var attachment = await db.FileAttachments.FindAsync([id], ct);
            if (attachment is null) continue;

            db.FileAttachments.Remove(attachment);
            DeletePhysicalFile(Path.Combine(UploadsPath, $"file_{id}"));
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    private void DeleteImageFiles(IEnumerable<Guid> ids, string survivingContent)
    {
        if (!Directory.Exists(UploadsPath)) return;

        foreach (var id in ids)
        {
            if (survivingContent.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var file = new DirectoryInfo(UploadsPath)
                .EnumerateFiles($"{id}.*")
                .FirstOrDefault();

            if (file is not null)
                DeletePhysicalFile(file.FullName);
        }
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
