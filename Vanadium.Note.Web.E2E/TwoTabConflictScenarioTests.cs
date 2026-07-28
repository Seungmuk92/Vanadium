using Microsoft.Playwright;
using NUnit.Framework;

namespace Vanadium.Note.Web.E2E;

/// <summary>
/// Scenario 2 — "two tabs edit the same note" (issue #308).
///
/// <para>
/// Tab A and Tab B open the same note. Tab B saves first, advancing the server row. When Tab A
/// then saves its stale copy the server rejects it with 409 and the editor must surface the
/// conflict banner (optimistic concurrency, issue #221) offering Force Save — never silently
/// clobbering Tab B's write. Force Save then overwrites and clears the banner.
/// </para>
/// </summary>
public sealed class TwoTabConflictScenarioTests : PlaywrightScenarioBase
{
    [Test]
    public async Task ConcurrentEdit_ShowsConflictBanner_ThenForceSaveResolves()
    {
        await using var context = await NewContextAsync(); // shared context = shared auth/token
        var tabA = await context.NewPageAsync();
        await LoginAsync(tabA);

        // Create the note in tab A and capture its editor URL.
        await tabA.GotoAsync("/editor");
        await tabA.FillAsync(".editor-title", "Two-tab conflict note");
        await tabA.ClickAsync(".tiptap-wrapper .ProseMirror");
        await tabA.Keyboard.TypeAsync("original body");
        // Exact-text match so this does not also hit the adjacent "Save & close" button.
        await tabA.ClickAsync("button:text-is('Save')");
        await tabA.WaitForURLAsync(u => u.Contains("/editor/"), new() { Timeout = 15_000 });
        var noteUrl = tabA.Url;

        // Let tab A go fully idle before the race. Typing armed a 1500ms debounced auto-save that
        // the explicit Save does NOT cancel; it fires a trailing PUT ~1.5s later. If that late
        // write lands after tab B's GET it advances the server row, so tab B's own save loses a
        // false 409. Waiting for the idle badge guarantees the trailing write has landed and the
        // server version is final. (Headed timing exposes this race that headless hid.)
        await WaitForEditorIdleAsync(tabA);

        // The optimistic-concurrency conflict banner shares its .conflict-banner class with the
        // stashed-draft banner (NoteEditor.razor), so scope to the one carrying Force Save.
        const string conflictBanner = ".conflict-banner:has(.btn-danger)";

        // Tab B opens the same note and saves an edit first.
        var tabB = await context.NewPageAsync();
        await tabB.GotoAsync(noteUrl);
        // Wait for Tab B's editor to actually LOAD the note (title populated from the server)
        // before editing, so its save carries the current version and lands a clean 200.
        await Assertions.Expect(tabB.Locator(".editor-title")).ToHaveValueAsync(
            "Two-tab conflict note", new() { Timeout = 15_000 });
        await tabB.FillAsync(".editor-title", "Edited by tab B");
        // Save and wait for the PUT to actually succeed (200) — the point at which the server row
        // advances. The banner-absent check alone does not wait for the round-trip, so without
        // this Tab A's stale PUT could reach the server first, match the version, and never 409.
        await tabB.RunAndWaitForResponseAsync(
            () => tabB.ClickAsync("button:text-is('Save')"),
            resp => resp.Url.Contains("/api/notes/")
                    && resp.Request.Method == "PUT"
                    && resp.Status == 200,
            new() { Timeout = 15_000 });
        // Tab B held the current version, so its own save must not have conflicted.
        await Assertions.Expect(tabB.Locator(conflictBanner)).Not.ToBeVisibleAsync();
        // Let tab B's own trailing auto-save settle too, so it cannot advance the row mid-way
        // through tab A's stale save below.
        await WaitForEditorIdleAsync(tabB);

        // Tab A now saves its stale copy → server 409 → conflict banner.
        await tabA.FillAsync(".editor-title", "Edited by tab A (stale)");
        await tabA.ClickAsync("button:text-is('Save')");
        await Assertions.Expect(tabA.Locator(conflictBanner)).ToBeVisibleAsync(
            new() { Timeout = 15_000 });

        // Force Save resolves the conflict and dismisses the banner.
        await tabA.ClickAsync($"{conflictBanner} .btn-danger");
        await Assertions.Expect(tabA.Locator(conflictBanner)).Not.ToBeVisibleAsync(
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Waits until the editor has no save pending or in flight. The save-status badge
    /// (<c>.save-status</c>) reads "Writing…"/"Saving…"/"Saved" while work is outstanding and
    /// clears to empty only after ~2s with no save — so an empty badge means every debounced
    /// auto-save (including the trailing one an explicit Save does not cancel) has landed and the
    /// server row is final.
    /// </summary>
    private static Task WaitForEditorIdleAsync(IPage page) =>
        Assertions.Expect(page.Locator(".save-status")).ToHaveTextAsync(
            string.Empty, new() { Timeout = 15_000 });
}
