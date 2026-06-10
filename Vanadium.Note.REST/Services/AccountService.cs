using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Account-level destructive operations. Currently exposes a single
/// "purge everything" routine used by the Settings page "Dangerous" section.
/// </summary>
public class AccountService(NoteDbContext db, ILogger<AccountService> logger)
{
    /// <summary>
    /// Permanently removes all content created by the given user: every note
    /// (including child notes and their label associations), labels, label
    /// categories, API tokens, and personal settings. The user account row
    /// itself is intentionally kept so the user can still log in.
    /// </summary>
    /// <remarks>
    /// This method only deletes database rows. Uploaded files and images on disk
    /// are <b>not</b> touched here — once the notes that referenced them are gone,
    /// their FileAttachment records (for attachments) and on-disk image files
    /// become orphaned, and the periodic
    /// <see cref="OrphanFileCleanupJob"/> reclaims both on its next run. Keeping
    /// disk work out of the request path avoids loading every note's HTML into
    /// memory, which does not scale for large accounts.
    /// </remarks>
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

        // The DB is configured with a retrying execution strategy
        // (EnableRetryOnFailure), which forbids user-initiated transactions
        // unless the whole unit runs through the strategy so it can be retried
        // atomically. Running every delete inside this delegate keeps a retry
        // correct.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
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
                "{Categories} category(ies), {Tokens} token(s), {Settings} settings row(s). " +
                "Orphaned files/images will be reclaimed by the periodic cleanup job.",
                userId, notesRemoved, labelsRemoved, categoriesRemoved, tokensRemoved, settingsRemoved);
        });

        return true;
    }
}
