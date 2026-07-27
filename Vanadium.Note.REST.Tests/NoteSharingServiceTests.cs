using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Service-level tests for note sharing (issue #94): enabling/disabling shares, token minting,
/// anonymous token resolution, link invalidation on unshare, and the server-owned nature of the
/// share fields (a client create/update can never mint or tamper with a token).
/// </summary>
public class NoteSharingServiceTests
{
    [Fact]
    public async Task SetShare_Public_MintsTokenAndMarksShared()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable");

        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);

        Assert.NotNull(info);
        Assert.True(info!.IsShared);
        Assert.Equal(ShareMode.Public, info.Mode);
        Assert.False(string.IsNullOrEmpty(info.Token));
        Assert.NotNull(info.SharedAt);
    }

    [Fact]
    public async Task GetSharedByToken_ResolvesSharedNote()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable", content: "<p>hello</p>");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Link);

        var resolved = await h.Notes.GetSharedByToken(info!.Token);

        Assert.NotNull(resolved);
        Assert.Equal(note.Id, resolved!.Id);
        Assert.Equal("Sharable", resolved.Title);
    }

    [Fact]
    public async Task GetSharedByToken_SanitizesLegacyContentBeforeReturning()
    {
        using var h = new TestHost();

        // Seed a "legacy" note directly, bypassing Create's persist-time sanitizer, so the stored
        // row carries active content the way a pre-sanitizer row would (issue #294).
        var note = new NoteItem
        {
            Id = Guid.NewGuid(),
            Title = "Legacy",
            Content = "<p>hello</p><script>alert(1)</script>",
            ContentText = "hello",
            UpdatedAt = DateTime.UtcNow,
            ShareToken = "legacy-token",
            ShareMode = ShareMode.Public,
            SharedAt = DateTime.UtcNow
        };
        h.Db.Notes.Add(note);
        await h.Db.SaveChangesAsync();

        var resolved = await h.Notes.GetSharedByToken("legacy-token");

        Assert.NotNull(resolved);
        Assert.DoesNotContain("<script", resolved!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hello", resolved.Content);

        // The in-memory read-time sanitize must NOT be flushed back to the stored row (AsNoTracking).
        var stored = await h.FindAsync(note.Id);
        Assert.Contains("<script", stored!.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReSanitizeAllContent_StripsActiveContentFromLegacyRows()
    {
        using var h = new TestHost();

        var dirty = new NoteItem
        {
            Id = Guid.NewGuid(),
            Title = "Legacy",
            Content = "<p>keep</p><script>alert(1)</script>",
            ContentText = "keep",
            UpdatedAt = DateTime.UtcNow
        };
        h.Db.Notes.Add(dirty);
        await h.Db.SaveChangesAsync();

        var updated = await h.Notes.ReSanitizeAllContentAsync();

        Assert.Equal(1, updated);
        var stored = await h.FindAsync(dirty.Id);
        Assert.DoesNotContain("<script", stored!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("keep", stored.Content);
        // ContentText is re-derived from the sanitized markup, keeping the Create/Update invariant.
        Assert.DoesNotContain("alert", stored.ContentText, StringComparison.OrdinalIgnoreCase);

        // Idempotent: a second pass finds nothing to change.
        Assert.Equal(0, await h.Notes.ReSanitizeAllContentAsync());
    }

    [Fact]
    public async Task ReSanitizeAllContent_IncludesArchivedAndSoftDeletedNotes()
    {
        using var h = new TestHost();

        var archived = new NoteItem
        {
            Id = Guid.NewGuid(),
            Title = "Archived",
            Content = "<p>a</p><script>alert(1)</script>",
            ContentText = "a",
            UpdatedAt = DateTime.UtcNow,
            ArchivedAt = DateTime.UtcNow,
            IsArchiveRoot = true
        };
        var deleted = new NoteItem
        {
            Id = Guid.NewGuid(),
            Title = "Deleted",
            Content = "<p>d</p><script>alert(2)</script>",
            ContentText = "d",
            UpdatedAt = DateTime.UtcNow,
            DeletedAt = DateTime.UtcNow,
            IsDeletionRoot = true
        };
        h.Db.Notes.AddRange(archived, deleted);
        await h.Db.SaveChangesAsync();

        var updated = await h.Notes.ReSanitizeAllContentAsync();

        Assert.Equal(2, updated);
        var storedArchived = await h.FindAsync(archived.Id);
        var storedDeleted = await h.FindAsync(deleted.Id);
        Assert.DoesNotContain("<script", storedArchived!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", storedDeleted!.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSharedByToken_WrongToken_ReturnsNull()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable");
        await h.Notes.SetShare(note.Id, ShareMode.Public);

        Assert.Null(await h.Notes.GetSharedByToken("does-not-exist"));
        Assert.Null(await h.Notes.GetSharedByToken(null));
        Assert.Null(await h.Notes.GetSharedByToken(""));
    }

    [Fact]
    public async Task Unshare_InvalidatesExistingLink()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);
        var token = info!.Token;

        // Link works while shared.
        Assert.NotNull(await h.Notes.GetSharedByToken(token));

        var unshared = await h.Notes.Unshare(note.Id);

        Assert.True(unshared);
        // The old link no longer resolves — the token was cleared.
        Assert.Null(await h.Notes.GetSharedByToken(token));

        var reloaded = await h.FindAsync(note.Id);
        Assert.Null(reloaded!.ShareToken);
        Assert.Equal(ShareMode.None, reloaded.ShareMode);
        Assert.Null(reloaded.SharedAt);
    }

    [Fact]
    public async Task SetShare_None_IsEquivalentToUnshare()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);
        var token = info!.Token;

        var cleared = await h.Notes.SetShare(note.Id, ShareMode.None);

        Assert.NotNull(cleared);
        Assert.False(cleared!.IsShared);
        Assert.Null(await h.Notes.GetSharedByToken(token));
    }

    [Fact]
    public async Task SetShare_SwitchingMode_KeepsSameToken()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable");
        var first = await h.Notes.SetShare(note.Id, ShareMode.Public);

        var second = await h.Notes.SetShare(note.Id, ShareMode.Link);

        Assert.Equal(first!.Token, second!.Token);
        Assert.Equal(ShareMode.Link, second.Mode);
    }

    [Fact]
    public async Task GetSharedByToken_ExcludesSoftDeletedNote()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);

        await h.Notes.Delete(note.Id); // move to recycle bin

        // A shared note that lands in the recycle bin must not leak via its link.
        Assert.Null(await h.Notes.GetSharedByToken(info!.Token));
    }

    [Fact]
    public async Task SetShare_NonexistentNote_ReturnsNull()
    {
        using var h = new TestHost();
        Assert.Null(await h.Notes.SetShare(Guid.NewGuid(), ShareMode.Public));
    }

    [Fact]
    public async Task Create_ForcesSharingOff_EvenIfPayloadSetsIt()
    {
        using var h = new TestHost();

        var created = await h.Notes.Create(new NoteItem
        {
            Title = "Tampered",
            Content = "<p>x</p>",
            ShareToken = "attacker-supplied-token",
            ShareMode = ShareMode.Public,
            SharedAt = DateTime.UtcNow
        });

        Assert.Null(created.ShareToken);
        Assert.Equal(ShareMode.None, created.ShareMode);
        Assert.Null(created.SharedAt);
        Assert.Null(await h.Notes.GetSharedByToken("attacker-supplied-token"));
    }

    [Fact]
    public async Task Update_DoesNotAlterShareState()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Sharable", content: "<p>old</p>");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);

        // A content edit that also tries to smuggle in different share fields.
        // UpdatedAt carries the version the client last saw (sharing does not bump it).
        await h.Notes.Update(note.Id, new NoteItem
        {
            Title = "Edited",
            Content = "<p>new</p>",
            UpdatedAt = note.UpdatedAt,
            ShareToken = "attacker-supplied-token",
            ShareMode = ShareMode.None,
            SharedAt = null
        });

        var reloaded = await h.FindAsync(note.Id);
        Assert.Equal("Edited", reloaded!.Title);
        // Share state is untouched by the update path.
        Assert.Equal(info!.Token, reloaded.ShareToken);
        Assert.Equal(ShareMode.Public, reloaded.ShareMode);
        Assert.NotNull(await h.Notes.GetSharedByToken(info.Token));
    }
}
