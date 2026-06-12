using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Service-level tests for the editor toggle/accordion/collapsible-heading
/// feature (spec T-15 .. T-17 in docs/plannings/note-toggle-feature.md).
/// The feature is editor-only; these tests pin the backend invariants it
/// relies on: ContentText extraction (search), and orphan-file GC treating
/// references inside collapsed toggles as live. PostgreSQL-only behavior
/// (trigram ILIKE search) is verified manually per repo convention.
/// </summary>
public class ToggleContentTests
{
    private const string ToggleHtml =
        "<div data-type=\"toggle\" data-open=\"false\" class=\"toggle-block\">" +
        "<div data-type=\"toggle-summary\" class=\"toggle-summary\">Deployment log 2026-06-11</div>" +
        "<div data-type=\"toggle-content\" class=\"toggle-content\">" +
        "<p>Rollout went fine except for the cache warmup</p>" +
        "<pre data-language=\"text\"><code class=\"language-text\">12:01:14 WARN cache stampede</code></pre>" +
        "</div></div>";

    private const string AccordionHtml =
        "<div data-type=\"accordion-group\" class=\"accordion-group\">" +
        "<div data-type=\"toggle\" data-open=\"true\" class=\"toggle-block\">" +
        "<div data-type=\"toggle-summary\" class=\"toggle-summary\">How to rotate JWT secret</div>" +
        "<div data-type=\"toggle-content\" class=\"toggle-content\"><p>Update AUTH_JWT_SECRET and restart</p></div>" +
        "</div>" +
        "<div data-type=\"toggle\" data-open=\"false\" class=\"toggle-block\">" +
        "<div data-type=\"toggle-summary\" class=\"toggle-summary\">How to reset dev DB</div>" +
        "<div data-type=\"toggle-content\" class=\"toggle-content\"><p>Drop schema then rerun migrations</p></div>" +
        "</div></div>";

    private const string CollapsibleHeadingHtml =
        "<h2 data-collapsible=\"true\" data-open=\"false\">Research notes</h2>" +
        "<p>hidden paragraph alpha</p>" +
        "<h3>Sub-finding</h3>" +
        "<p>hidden paragraph beta</p>" +
        "<h2>Next section</h2>";

    // ── T-15: StripHtml over toggle/accordion/heading fixtures ──────────────

    [Fact]
    public async Task ContentText_ToggleFixture_ExtractsSummaryAndHiddenBody_NoAttributeFragments()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var note = await h.CreateNoteAsync(user.Id, "Toggle note", content: ToggleHtml);
        var saved = await h.FindAsync(note.Id);

        Assert.Contains("Deployment log 2026-06-11", saved!.ContentText);
        Assert.Contains("Rollout went fine except for the cache warmup", saved.ContentText);
        Assert.Contains("12:01:14 WARN cache stampede", saved.ContentText);

        // Attribute values must never leak into the searchable text.
        Assert.DoesNotContain("data-open", saved.ContentText);
        Assert.DoesNotContain("data-type", saved.ContentText);
        Assert.DoesNotContain("toggle-block", saved.ContentText);
        Assert.DoesNotContain("toggle-summary", saved.ContentText);
    }

    [Fact]
    public async Task ContentText_AccordionFixture_ExtractsAllItems()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var note = await h.CreateNoteAsync(user.Id, "FAQ", content: AccordionHtml);
        var saved = await h.FindAsync(note.Id);

        Assert.Contains("How to rotate JWT secret", saved!.ContentText);
        Assert.Contains("Update AUTH_JWT_SECRET and restart", saved.ContentText);
        Assert.Contains("How to reset dev DB", saved.ContentText);
        Assert.Contains("Drop schema then rerun migrations", saved.ContentText);
        Assert.DoesNotContain("accordion-group", saved.ContentText);
        Assert.DoesNotContain("data-open", saved.ContentText);
    }

    [Fact]
    public async Task ContentText_CollapsibleHeadingFixture_ExtractsFoldedSection()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var note = await h.CreateNoteAsync(user.Id, "Folded heading", content: CollapsibleHeadingHtml);
        var saved = await h.FindAsync(note.Id);

        Assert.Contains("Research notes", saved!.ContentText);
        Assert.Contains("hidden paragraph alpha", saved.ContentText);
        Assert.Contains("Sub-finding", saved.ContentText);
        Assert.Contains("hidden paragraph beta", saved.ContentText);
        Assert.DoesNotContain("data-collapsible", saved.ContentText);
        Assert.DoesNotContain("data-open", saved.ContentText);
    }

    [Fact]
    public async Task ContentText_UpdatePath_RecomputedForToggleContent()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Note", content: "<p>plain</p>");

        note.Content = ToggleHtml;
        var (updated, conflict, archived) = await h.Notes.Update(user.Id, note.Id, note);

        Assert.NotNull(updated);
        Assert.False(conflict);
        Assert.False(archived);
        var saved = await h.FindAsync(note.Id);
        Assert.Contains("12:01:14 WARN cache stampede", saved!.ContentText);
        Assert.DoesNotContain("data-open", saved.ContentText);
    }

    // ── T-16: search readiness for text hidden in a collapsed toggle ────────
    //
    // The ContentText half (term present despite data-open="false") is the
    // SQLite-verifiable part; the ILike query itself is PostgreSQL-only and
    // verified manually, mirroring the ArchiveServiceTests convention.

    [Fact]
    public async Task Search_TermOnlyInsideCollapsedToggle_IsPresentInContentText()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var note = await h.CreateNoteAsync(user.Id, "Untitled", content: ToggleHtml);
        var saved = await h.FindAsync(note.Id);

        // "stampede" exists only inside the collapsed (data-open="false") body.
        Assert.DoesNotContain("stampede", saved!.Title);
        Assert.Contains("stampede", saved.ContentText);
    }

    [Fact(Skip = "Search inclusion uses EF.Functions.ILike (PostgreSQL trigram); verified manually via Swagger/UI.")]
    public Task Search_TermOnlyInsideCollapsedToggle_NoteMatches() => Task.CompletedTask;

    // ── T-17: orphan cleanup treats refs inside collapsed toggles as live ───

    [Fact]
    public async Task OrphanCleanup_FileReferencedOnlyInsideCollapsedToggle_IsKept()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var attachment = new FileAttachment { OriginalName = "full-log.txt", ContentType = "text/plain" };
        h.Db.FileAttachments.Add(attachment);
        await h.Db.SaveChangesAsync();

        var physicalPath = Path.Combine(h.ContentRoot, "uploads", $"file_{attachment.Id}");
        await File.WriteAllTextAsync(physicalPath, "log body");

        var content =
            "<div data-type=\"toggle\" data-open=\"false\" class=\"toggle-block\">" +
            "<div data-type=\"toggle-summary\" class=\"toggle-summary\">Logs</div>" +
            "<div data-type=\"toggle-content\" class=\"toggle-content\">" +
            $"<p><a class=\"file-attachment\" data-filename=\"full-log.txt\" href=\"https://localhost/api/files/{attachment.Id}\">📎 full-log.txt</a></p>" +
            "</div></div>";
        await h.CreateNoteAsync(user.Id, "Note with hidden attachment", content: content);

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.NotNull(await h.Db.FileAttachments.FindAsync(attachment.Id));
        Assert.True(File.Exists(physicalPath));
    }

    [Fact]
    public async Task OrphanCleanup_UnreferencedFile_StillRemoved_SanityCheck()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();

        var orphan = new FileAttachment { OriginalName = "orphan.txt", ContentType = "text/plain" };
        h.Db.FileAttachments.Add(orphan);
        await h.Db.SaveChangesAsync();
        var orphanPath = Path.Combine(h.ContentRoot, "uploads", $"file_{orphan.Id}");
        await File.WriteAllTextAsync(orphanPath, "orphan");

        await h.CreateNoteAsync(user.Id, "Unrelated", content: "<p>no references here</p>");

        await h.FileCleanup.DeleteAllOrphansAsync();

        Assert.Null(await h.Db.FileAttachments.FindAsync(orphan.Id));
        Assert.False(File.Exists(orphanPath));
    }
}
