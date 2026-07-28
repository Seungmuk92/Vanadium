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

        // The optimistic-concurrency conflict banner shares its .conflict-banner class with the
        // stashed-draft banner (NoteEditor.razor), so scope to the one carrying Force Save.
        const string conflictBanner = ".conflict-banner:has(.btn-danger)";

        // Tab B opens the same note and saves an edit first.
        var tabB = await context.NewPageAsync();
        await tabB.GotoAsync(noteUrl);
        await tabB.FillAsync(".editor-title", "Edited by tab B");
        await tabB.ClickAsync("button:text-is('Save')");
        await Assertions.Expect(tabB.Locator(conflictBanner)).Not.ToBeVisibleAsync();

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
}
