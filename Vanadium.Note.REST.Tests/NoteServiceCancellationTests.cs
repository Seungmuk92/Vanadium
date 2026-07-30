using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Verifies that a CancellationToken handed to NoteService reaches the EF Core
/// calls: an already-cancelled token must surface as an OperationCanceledException
/// rather than being ignored. This guards the token plumbing (#140) — without it,
/// the methods would run to completion regardless of the caller's cancellation.
/// </summary>
public class NoteServiceCancellationTests
{
    private static CancellationToken Cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact]
    public async Task GetPaged_WithCancelledToken_Throws()
    {
        using var h = new TestHost();
        await h.CreateNoteAsync("A");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Notes.GetPaged(1, 30, null, "date", "desc", null, Cancelled()));
    }

    [Fact]
    public async Task Get_WithCancelledToken_Throws()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("A");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Notes.Get(note.Id, Cancelled()));
    }

    [Fact]
    public async Task Create_WithCancelledToken_Throws()
    {
        using var h = new TestHost();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Notes.Create(new NoteItem { Title = "A", Content = "" }, Cancelled()));
    }

    [Fact]
    public async Task Delete_WithCancelledToken_Throws()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("A");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Notes.Delete(note.Id, Cancelled()));
    }
}
