using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Account-level destructive operations. Currently exposes a single
/// "purge everything" routine used by the Settings page "Dangerous" section.
/// </summary>
public class AccountService(NoteDbContext db, FileCleanupService fileCleanup, ILogger<AccountService> logger)
{
    /// <summary>
    /// Permanently removes all content created by the given user: every note
    /// (including child notes and their label associations), labels, label
    /// categories, API tokens, and personal settings. The user account row
    /// itself is intentionally kept so the user can still log in.
    /// Uploaded files/images that the deleted notes referenced and that are no
    /// longer referenced by any surviving note are removed from disk afterwards.
    /// </summary>
    /// <returns><c>false</c> if no matching user exists; otherwise <c>true</c>.</returns>
    public async Task<bool> PurgeAllDataAsync(string username, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null)
        {
            logger.LogWarning("Purge requested for unknown user '{Username}'.", username);
            return false;
        }

        var userId = user.Id;
        var deletedContent = string.Empty;

        // The DB is configured with a retrying execution strategy
        // (EnableRetryOnFailure), which forbids user-initiated transactions
        // unless the whole unit runs through the strategy so it can be retried
        // atomically. Capturing the content and running every delete inside this
        // delegate keeps a retry correct.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Capture all of the user's note content up front so we can clean up
            // the files it referenced after the rows are gone.
            deletedContent = string.Join(' ',
                await db.Notes.Where(n => n.UserId == userId)
                              .Select(n => n.Content)
                              .ToListAsync(ct));

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Notes first — the DB cascades NoteLabels (via NoteId) and any child
            // notes (self-referencing ParentNoteId cascade), all of which belong
            // to this same user and are included in the predicate regardless.
            var notesRemoved = await db.Notes.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);

            // Labels before categories: a category cascade would also clear labels,
            // but deleting labels explicitly first keeps the intent obvious.
            var labelsRemoved = await db.Labels.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
            var categoriesRemoved = await db.LabelCategories.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);

            var tokensRemoved = await db.ApiTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);

            // UserSettings is keyed by Username, not UserId.
            var settingsRemoved = await db.UserSettings.Where(s => s.Username == username).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Purged all data for user {UserId}: {Notes} note(s), {Labels} label(s), " +
                "{Categories} category(ies), {Tokens} token(s), {Settings} settings row(s).",
                userId, notesRemoved, labelsRemoved, categoriesRemoved, tokensRemoved, settingsRemoved);
        });

        // Disk cleanup runs after the DB rows are committed so the orphan check
        // sees the post-deletion state. Physical file removal is not part of the
        // transaction (it cannot be rolled back), which is why it goes last.
        await fileCleanup.DeleteOrphanedFromContentAsync(deletedContent, ct);

        return true;
    }
}
