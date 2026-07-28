using Microsoft.Playwright;
using NUnit.Framework;

namespace Vanadium.Note.Web.E2E;

/// <summary>
/// Scenario 3 — "Korean mention typed through an IME" (issue #308).
///
/// <para>
/// Mentioning a note by a Hangul title exercises the IME path: an IME commits composed text via
/// <c>input</c> events rather than discrete <c>keydown</c>s, and the mention suggestion plugin must
/// still search and match on that composed text. This scenario opens the mention menu with
/// <c>@</c>, commits Korean via <see cref="IKeyboard.InsertTextAsync"/> (Playwright's closest proxy
/// for an IME commit), and verifies the Korean-titled note is found and inserted as a
/// <c>a.note-mention</c> node carrying the Hangul title as text.
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

        // In a fresh note, open the mention menu and commit the Korean query through the IME path.
        await page.GotoAsync("/editor");
        await page.ClickAsync(".tiptap-wrapper .ProseMirror");
        // TypeAsync fires the full keydown/keypress/input sequence, which reliably drives
        // ProseMirror's input handling and opens the suggestion menu; a bare PressAsync("@")
        // does not always register as editor input.
        await page.Keyboard.TypeAsync("@");
        await page.Keyboard.InsertTextAsync("회의"); // composed (IME-style) commit, not per-key keydowns

        // The suggestion menu must surface the Korean-titled note.
        var menuItem = page.Locator(".mention-menu-item", new() { HasTextString = "회의록" });
        await Assertions.Expect(menuItem).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await menuItem.First.ClickAsync();

        // The committed mention is a note-mention node bearing the Hangul title.
        var mention = page.Locator("a.note-mention");
        await Assertions.Expect(mention).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That(await mention.First.InnerTextAsync(), Does.Contain("회의록"));
    }
}
