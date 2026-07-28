using Microsoft.Playwright;
using NUnit.Framework;

namespace Vanadium.Note.Web.E2E;

/// <summary>
/// Scenario 3 — "Korean mention typed by a Hangul title" (issue #308).
///
/// <para>
/// Mentioning a note by a Hangul title must search, match, and insert on Korean text. The scenario
/// opens the mention menu with <c>@</c> and types the Hangul query. The mention suggestion plugin
/// derives its query from ProseMirror document changes (not raw keydowns), so driving the query
/// with <see cref="IKeyboard.TypeAsync"/> exercises the same search/match path an IME commit would.
/// (<see cref="IKeyboard.InsertTextAsync"/> — a CDP <c>Input.insertText</c> — was tried as an
/// IME-commit proxy but does not reliably update an already-open suggestion in headless Chromium;
/// full native IME composition is not reproducible through Playwright's public API.)
/// </para>
/// </summary>
public sealed class KoreanMentionImeScenarioTests : PlaywrightScenarioBase
{
    private const string KoreanTitle = "회의록 2026";

    [Test]
    public async Task MentionByKoreanTitle_ViaImeCommit_InsertsMentionNode()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page);

        // Seed a note whose title is Korean so the mention search has a Hangul target.
        await page.GotoAsync("/editor");
        await page.FillAsync(".editor-title", KoreanTitle);
        await page.ClickAsync(".tiptap-wrapper .ProseMirror");
        await page.Keyboard.TypeAsync("target note body");
        await page.ClickAsync("button:text-is('Save')"); // exact match, not "Save & close"
        await page.WaitForURLAsync(u => u.Contains("/editor/"), new() { Timeout = 15_000 });

        // In a fresh note, open the mention menu and search by the Korean title.
        await page.GotoAsync("/editor");
        await page.ClickAsync(".tiptap-wrapper .ProseMirror");
        // TypeAsync fires the full keydown/keypress/input sequence, which reliably drives
        // ProseMirror's input handling; a bare PressAsync("@") does not always register.
        await page.Keyboard.TypeAsync("@");
        // Checkpoint: '@' alone opens the menu (empty query → recent notes). Asserting this
        // separately pinpoints whether a later failure is the trigger or the Hangul search.
        await Assertions.Expect(page.Locator(".mention-menu")).ToBeVisibleAsync(
            new() { Timeout = 15_000 });

        // Type the Hangul query so the suggestion searches over Korean text.
        await page.Keyboard.TypeAsync("회의");

        // The suggestion menu must surface the Korean-titled note. Match .First: repeated seed
        // runs can leave several identically-titled "회의록" notes, and any one proves the Hangul
        // search matched — clicking it inserts a mention carrying the Korean title.
        var menuItem = page.Locator(".mention-menu-item", new() { HasTextString = "회의록" }).First;
        await Assertions.Expect(menuItem).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await menuItem.ClickAsync();

        // The committed mention is a note-mention node bearing the Hangul title.
        var mention = page.Locator("a.note-mention");
        await Assertions.Expect(mention).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That(await mention.First.InnerTextAsync(), Does.Contain("회의록"));
    }
}
