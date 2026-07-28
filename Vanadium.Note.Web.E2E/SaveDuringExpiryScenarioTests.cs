using Microsoft.Playwright;
using NUnit.Framework;

namespace Vanadium.Note.Web.E2E;

/// <summary>
/// Scenario 1 — "save while the session expires" (issue #308).
///
/// <para>
/// The user is editing a note when their JWT expires mid-session. The next auto-save comes back
/// 401; the app must clear the token and redirect to <c>/login</c> carrying a <c>returnUrl</c>
/// (issues #117/#297) rather than silently dropping the edit or flashing a broken logged-in shell.
/// The expiry is forced by overwriting the stored <c>authToken</c> with an already-expired JWT.
/// </para>
/// </summary>
public sealed class SaveDuringExpiryScenarioTests : PlaywrightScenarioBase
{
    [Test]
    public async Task SaveAfterTokenExpires_RedirectsToLoginWithReturnUrl()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page);

        // Create a note and open its editor.
        await page.GotoAsync("/editor");
        await page.FillAsync(".editor-title", "Expiry scenario note");
        await page.ClickAsync(".tiptap-wrapper .ProseMirror");
        await page.Keyboard.TypeAsync("draft body before expiry");

        // Force the session to expire: replace the stored JWT with one whose exp is in the past.
        await page.EvaluateAsync(
            @"() => {
                const [h, , s] = (localStorage.getItem('authToken') || 'h.e.s').split('.');
                const past = Math.floor(Date.now() / 1000) - 3600;
                const b64url = obj => btoa(JSON.stringify(obj)).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');
                localStorage.setItem('authToken', `${h || 'h'}.${b64url({ name: 'owner', exp: past })}.${s || 's'}`);
            }");

        // Trigger a save with the now-expired token. Exact-text match so this does not also
        // hit the adjacent "Save & close" button.
        await page.ClickAsync("button:text-is('Save')");

        // The 401 handler must bounce to login and preserve where we were.
        await page.WaitForURLAsync(u => u.Contains("/login"), new() { Timeout = 15_000 });
        Assert.That(page.Url, Does.Contain("returnUrl"));
        Assert.That(Uri.UnescapeDataString(page.Url), Does.Contain("editor"));
    }
}
