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
/// The expiry is simulated by intercepting the save call and returning 401 — the same signal a
/// real expired-JWT request receives from the server.
/// </para>
///
/// <para>
/// Overwriting the stored <c>authToken</c> in <c>localStorage</c> is deliberately NOT used:
/// <c>TokenStore</c> caches the token in memory after the first read, and the same-tab
/// <c>storage</c> event never fires for a change made by the tab itself, so the app would keep
/// sending the still-valid cached token and the save would succeed. Fulfilling the request with
/// 401 reproduces exactly what an expired session does — the server rejecting the write — without
/// depending on those cache internals.
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

        // Force the session to expire: the next note save comes back 401, exactly as it would
        // if the JWT had expired. AuthTokenHandler still sends the (valid) cached token, so its
        // 401 branch — clear token + redirect to /login?returnUrl — is what we exercise here.
        await page.RouteAsync("**/api/notes**", route =>
            route.FulfillAsync(new RouteFulfillOptions { Status = 401 }));

        // Trigger a save with the now-expired session. Exact-text match so this does not also
        // hit the adjacent "Save & close" button.
        await page.ClickAsync("button:text-is('Save')");

        // The 401 handler must bounce to login and preserve where we were.
        await page.WaitForURLAsync(u => u.Contains("/login"), new() { Timeout = 15_000 });
        Assert.That(page.Url, Does.Contain("returnUrl"));
        Assert.That(Uri.UnescapeDataString(page.Url), Does.Contain("editor"));
    }
}
