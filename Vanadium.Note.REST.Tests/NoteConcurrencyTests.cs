using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Concurrency tests for issue #130: NoteItem.UpdatedAt is a DB-level EF Core
/// concurrency token (<see cref="System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute"/>),
/// so a stale write is rejected by the UPDATE's WHERE clause rather than an
/// in-memory read-then-write compare. Behavior is provider-independent (the
/// token rides in the generated SQL), so the SQLite in-memory host is faithful.
/// </summary>
public class NoteConcurrencyTests
{
    // ── The token is enforced by the database, not by an in-memory compare ────

    [Fact]
    public async Task ConcurrencyToken_ConcurrentModification_ThrowsDbUpdateConcurrencyException()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<NoteDbContext>().UseSqlite(connection).Options;

        Guid id;
        using (var seed = new NoteDbContext(options))
        {
            seed.Database.EnsureCreated();
            var n = new NoteItem
            {
                Title = "Seed",
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            seed.Notes.Add(n);
            await seed.SaveChangesAsync();
            id = n.Id;
        }

        // Context A loads the row; its original token value is the seeded one.
        using var ctxA = new NoteDbContext(options);
        var a = await ctxA.Notes.FirstAsync(n => n.Id == id);

        // A concurrent writer (context B) wins the race and bumps UpdatedAt.
        using (var ctxB = new NoteDbContext(options))
        {
            var b = await ctxB.Notes.FirstAsync(n => n.Id == id);
            b.Title = "Won the race";
            b.UpdatedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
            await ctxB.SaveChangesAsync();
        }

        // A now saves its stale copy: the token in the WHERE clause matches 0 rows.
        a.Title = "Lost the race";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxA.SaveChangesAsync());
    }

    // ── Service surface: the 409 Conflict path is preserved ──────────────────

    [Fact]
    public async Task Update_StaleClientVersion_ReturnsConflict_AndLeavesNoteUnchanged()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Original", content: "<p>original</p>");

        // The client claims a version the server row never had.
        var stale = note.UpdatedAt.AddSeconds(-5);
        var (updated, conflict, archived) = await h.Notes.Update(note.Id,
            new NoteItem { Title = "Changed", Content = "<p>changed</p>", UpdatedAt = stale });

        Assert.Null(updated);
        Assert.True(conflict);
        Assert.False(archived);

        var fresh = await h.FindAsync(note.Id);
        Assert.Equal("Original", fresh!.Title);
    }

    [Fact]
    public async Task Update_MatchingClientVersion_Succeeds_AndAdvancesVersion()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Original", content: "<p>original</p>");

        var (updated, conflict, archived) = await h.Notes.Update(note.Id,
            new NoteItem { Title = "Changed", Content = "<p>changed</p>", UpdatedAt = note.UpdatedAt });

        Assert.False(conflict);
        Assert.False(archived);
        Assert.NotNull(updated);
        Assert.Equal("Changed", updated!.Title);
        // The version stamp is refreshed on save and never moves backward.
        // (Not asserted strictly greater: DateTime.UtcNow can return the same
        // tick for two saves that land within the system timer's resolution.)
        Assert.True(updated.UpdatedAt >= note.UpdatedAt);
    }

    [Fact]
    public async Task Update_DefaultVersion_ForceSaves_BypassingTheConflictCheck()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Original", content: "<p>original</p>");

        // A default UpdatedAt is the intentional force-save bypass.
        var (updated, conflict, archived) = await h.Notes.Update(note.Id,
            new NoteItem { Title = "Force", Content = "<p>f</p>", UpdatedAt = default });

        Assert.False(conflict);
        Assert.False(archived);
        Assert.NotNull(updated);
        Assert.Equal("Force", updated!.Title);
    }
}
