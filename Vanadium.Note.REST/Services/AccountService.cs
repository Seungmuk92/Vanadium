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
    /// Permanently removes all content: every note (including child notes and their
    /// label associations), labels, label categories, API tokens, and personal
    /// settings. The owner's password lives in configuration, so login is unaffected.
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
    /// <returns>Always <c>true</c> once the purge completes.</returns>
    public async Task<bool> PurgeAllDataAsync(CancellationToken ct = default)
    {
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
            // notes (self-referencing ParentNoteId cascade).
            // IgnoreQueryFilters: soft-deleted notes must also be wiped — the global
            // soft-delete filter would otherwise let them survive an account wipe.
            var notesRemoved = await db.Notes.IgnoreQueryFilters().ExecuteDeleteAsync(ct);

            // Labels before categories: a category cascade would also clear labels,
            // but deleting labels explicitly first keeps the intent obvious.
            var labelsRemoved = await db.Labels.ExecuteDeleteAsync(ct);
            var categoriesRemoved = await db.LabelCategories.ExecuteDeleteAsync(ct);

            var tokensRemoved = await db.ApiTokens.ExecuteDeleteAsync(ct);

            var settingsRemoved = await db.UserSettings.ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Purged all data: {Notes} note(s), {Labels} label(s), " +
                "{Categories} category(ies), {Tokens} token(s), {Settings} settings row(s). " +
                "Orphaned files/images will be reclaimed by the periodic cleanup job.",
                notesRemoved, labelsRemoved, categoriesRemoved, tokensRemoved, settingsRemoved);
        });

        return true;
    }
}
