using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression coverage for issue #298: title-change propagation
/// (<c>NoteService.UpdatePageLinkReferences</c>) must advance each referencing note's microsecond
/// <see cref="NoteItem.UpdatedAt"/> and must not overwrite a concurrent edit to a referencing note.
/// Before the fix, propagation neither bumped <c>UpdatedAt</c> nor tolerated a conflict — a
/// concurrent edit surfaced as an uncaught <see cref="DbUpdateConcurrencyException"/> that failed
/// the whole request after the title note had already been saved.
/// </summary>
public class TitlePropagationConcurrencyTests
{
    // A page-link node in note B that references note A, showing A's title. Matches the shape the
    // editor serializes and the data-note-id="{id}" scan that WhereReferencesNote keys on.
    private static string PageLinkTo(Guid referencedId, string title) =>
        $"<div data-type=\"page-link\" data-note-id=\"{referencedId}\" data-title=\"{title}\" class=\"page-link-block\">" +
        $"<span class=\"page-link-icon\">📄</span><span class=\"page-link-title\">{title}</span></div>";

    private static async Task<(NoteItem? Note, bool Conflict, bool Archived)> ChangeTitleAsync(
        TestHost h, NoteItem note, string newTitle) =>
        await h.Notes.Update(note.Id,
            new NoteItem { Title = newTitle, Content = note.Content, UpdatedAt = note.UpdatedAt });

    [Fact]
    public async Task TitleChange_PropagatesToReferencingNote_AndBumpsUpdatedAt()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Old Title");
        var referrer = await h.CreateNoteAsync("Referrer", content: PageLinkTo(target.Id, "Old Title"));
        var versionBefore = referrer.UpdatedAt;

        var (_, conflict, _) = await ChangeTitleAsync(h, target, "New Title");
        Assert.False(conflict);

        var fresh = await h.FindAsync(referrer.Id);
        Assert.Contains("New Title", fresh!.Content);       // title propagated into the page-link
        Assert.DoesNotContain("Old Title", fresh.Content);
        Assert.True(fresh.UpdatedAt > versionBefore);       // AC1: UpdatedAt advanced
    }

    [Fact]
    public async Task TitleChange_ConcurrentEditOnReferencingNote_IsNotOverwritten()
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Old Title");
        var referrer = await h.CreateNoteAsync("Referrer",
            content: PageLinkTo(target.Id, "Old Title") + "<p>original body</p>");

        // Simulate a concurrent editor saving note B (the referrer) AFTER the service last read it:
        // a raw UPDATE advances the DB row's UpdatedAt and content, bypassing the change tracker, so
        // the still-tracked instance carries a stale concurrency token — exactly the mid-propagation
        // race the fix must survive. The page-link is kept so the note still matches the reference scan.
        var concurrentContent = PageLinkTo(target.Id, "Old Title") + "<p>CONCURRENT EDIT</p>";
        var concurrentVersion = DateTime.UtcNow.AddMinutes(5);
        await h.Db.Database.ExecuteSqlAsync(
            $"UPDATE Notes SET Content = {concurrentContent}, UpdatedAt = {concurrentVersion} WHERE Id = {referrer.Id}");

        // Changing A's title triggers propagation to B. It must not throw and must not lose the edit.
        var (_, conflict, _) = await ChangeTitleAsync(h, target, "New Title");
        Assert.False(conflict);

        var fresh = await h.FindAsync(referrer.Id);
        Assert.Contains("CONCURRENT EDIT", fresh!.Content);  // the concurrent edit survived (not overwritten)
        Assert.Contains("New Title", fresh.Content);         // and the title was still layered on top
        Assert.DoesNotContain("original body", fresh.Content);
    }
}
