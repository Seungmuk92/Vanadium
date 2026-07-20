using System.Globalization;
using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Tracks which notes are owned by a live editor, in <c>localStorage</c> so the claim is
/// visible to every tab on the origin (issue #211).
/// </summary>
/// <remarks>
/// <para>
/// The offline save queue lives in <c>localStorage</c> and is therefore shared by all
/// tabs, so an in-memory claim set would only have excluded the flush running in the SAME
/// tab. With tab A editing a note and tab B sitting on another page, B's reconnect flush
/// saw no claim, replayed the parked (older) content, and left A to take a 409 — a
/// conflict banner for an edit nobody else made, with the older version on the server.
/// </para>
/// <para>
/// A claim carries an expiry rather than being a plain flag, because a tab that crashes
/// or is killed never gets to release it and would otherwise block that note's queue
/// entry forever. The owning editor refreshes its claim on a heartbeat well inside
/// <see cref="Ttl"/>; once the heartbeat stops, the claim lapses and the queue takes the
/// note back over.
/// </para>
/// </remarks>
public sealed class NoteClaimStore(IJSRuntime js, ILogger<NoteClaimStore> logger)
{
    private const string KeyPrefix = "vanadium.note-claim.v1.";

    /// <summary>How long a claim stays valid without a refresh.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);

    private static string KeyFor(Guid noteId) => KeyPrefix + noteId.ToString("D");

    /// <summary>Claims <paramref name="noteId"/>, or extends an existing claim.</summary>
    public async Task ClaimAsync(Guid noteId)
    {
        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(Ttl).ToUnixTimeMilliseconds();
            await js.InvokeVoidAsync(
                "localStorage.setItem",
                KeyFor(noteId),
                expiresAt.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to claim note {NoteId} for the open editor.", noteId);
        }
    }

    public async Task ReleaseAsync(Guid noteId)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", KeyFor(noteId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release the editor claim on note {NoteId}.", noteId);
        }
    }

    /// <summary>
    /// Whether an editor in any tab currently owns <paramref name="noteId"/>. An expired
    /// claim is treated as absent and cleaned up on the way out.
    /// </summary>
    public async Task<bool> IsClaimedAsync(Guid noteId)
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", KeyFor(noteId));
            if (string.IsNullOrEmpty(raw)
                || !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAt))
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiresAt)
            {
                await ReleaseAsync(noteId);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // Reporting "claimed" on a storage failure would strand the entry forever.
            // Falling back to "not claimed" at worst restores the pre-claim race, which
            // optimistic concurrency still catches.
            logger.LogError(ex, "Failed to read the editor claim on note {NoteId}.", noteId);
            return false;
        }
    }
}
