using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Owns anonymous note sharing (issue #311 split out of <see cref="NoteService"/>): minting and
/// clearing share tokens, resolving a note by its public token for the anonymous read path, and the
/// one-time content re-sanitization backfill. <see cref="NoteService"/> delegates its share methods
/// here so the public API — and the controller/test surface — is unchanged.
/// </summary>
public class NoteShareService(
    NoteDbContext db,
    IHtmlSanitizerService htmlSanitizer,
    ILogger<NoteShareService> logger)
{
    /// <summary>
    /// Sets a note's share mode. Enabling sharing mints a fresh unguessable token the first time;
    /// switching between share modes keeps the same token (and link). <see cref="ShareMode.None"/>
    /// is treated as unshare. Returns the resulting <see cref="ShareInfo"/>, or null if the note
    /// does not exist (soft-deleted notes are hidden by the global filter). Does NOT bump
    /// <c>UpdatedAt</c> — sharing is metadata and must not reorder the note in date-sorted lists.
    /// </summary>
    public async Task<ShareInfo?> SetShare(Guid id, ShareMode mode, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return null;

        if (mode == ShareMode.None)
        {
            ClearShare(note);
        }
        else
        {
            if (string.IsNullOrEmpty(note.ShareToken))
            {
                note.ShareToken = GenerateShareToken();
                note.SharedAt = NoteReferenceRewriter.UtcNowMicroseconds();
            }
            note.ShareMode = mode;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Note {NoteId} share mode set to {ShareMode}.", id, note.ShareMode);
        return ToShareInfo(note);
    }

    /// <summary>
    /// Disables sharing for a note, clearing its token so any previously issued link stops working
    /// immediately. Returns false when the note does not exist. Idempotent: unsharing an already
    /// un-shared note succeeds and reports success.
    /// </summary>
    public async Task<bool> Unshare(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return false;

        ClearShare(note);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Note {NoteId} unshared.", id);
        return true;
    }

    /// <summary>Returns the current share status of a note, or null if it does not exist.</summary>
    public async Task<ShareInfo?> GetShareInfo(Guid id, CancellationToken ct = default)
    {
        var note = await db.Notes
            .Select(n => new { n.Id, n.ShareToken, n.ShareMode, n.SharedAt })
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return null;
        return new ShareInfo
        {
            IsShared = note.ShareMode != ShareMode.None && !string.IsNullOrEmpty(note.ShareToken),
            Mode = note.ShareMode,
            Token = note.ShareToken,
            SharedAt = note.SharedAt
        };
    }

    /// <summary>
    /// Resolves a shared note by its public token for anonymous read access. Returns null when the
    /// token is empty, does not match any note, or the note is not currently shared. Soft-deleted
    /// notes are excluded by the global query filter, so an unshared-then-deleted link cannot leak.
    /// </summary>
    public async Task<NoteItem?> GetSharedByToken(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var note = await db.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n =>
                n.ShareToken == token && n.ShareMode != ShareMode.None, ct);
        if (note is null) return null;
        // Defense in depth (issue #294): the persist-time sanitizer runs only on Create/Update,
        // so legacy rows saved before it — or any future gap — could still carry active content.
        // Sanitize once more right before the note leaves the anonymous read path so a shared
        // page can never serve script/event handlers. AsNoTracking keeps this in-memory pass
        // from being flushed back to the row (which would desync the persisted ContentText).
        note.Content = htmlSanitizer.Sanitize(note.Content);
        return note;
    }

    /// <summary>
    /// One-time backfill (issue #294): re-sanitize every stored note's <c>Content</c> so legacy
    /// rows saved before the persist-time sanitizer can never serve active content on the
    /// anonymous share path. Archived and soft-deleted notes are included
    /// (<c>IgnoreQueryFilters</c>) because either can be restored/re-shared later. Idempotent:
    /// rows whose sanitized content is unchanged are left untouched, and <c>ContentText</c> is
    /// re-derived only when <c>Content</c> actually changed — the same Content/ContentText
    /// invariant the Create/Update paths maintain (the StripHtml derivation itself is unchanged).
    /// Returns the number of notes updated.
    /// </summary>
    public async Task<int> ReSanitizeAllContentAsync(CancellationToken ct = default)
    {
        var notes = await db.Notes.IgnoreQueryFilters().ToListAsync(ct);
        var changed = 0;
        foreach (var note in notes)
        {
            var sanitized = htmlSanitizer.Sanitize(note.Content);
            if (string.Equals(sanitized, note.Content, StringComparison.Ordinal)) continue;
            note.Content = sanitized;
            note.ContentText = NoteReferenceRewriter.StripHtml(sanitized);
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    private static void ClearShare(NoteItem note)
    {
        note.ShareToken = null;
        note.ShareMode = ShareMode.None;
        note.SharedAt = null;
    }

    private static ShareInfo ToShareInfo(NoteItem note) => new()
    {
        IsShared = note.ShareMode != ShareMode.None && !string.IsNullOrEmpty(note.ShareToken),
        Mode = note.ShareMode,
        Token = note.ShareToken,
        SharedAt = note.SharedAt
    };

    // 128 bits of randomness in the GUID makes the token unguessable; "N" yields 32 hex chars.
    private static string GenerateShareToken() => Guid.NewGuid().ToString("N");
}
