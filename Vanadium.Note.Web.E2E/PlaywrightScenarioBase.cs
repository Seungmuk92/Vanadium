using Microsoft.Playwright;
using NUnit.Framework;

namespace Vanadium.Note.Web.E2E;

/// <summary>
/// Base fixture for the Playwright worst-path scenarios (issue #308).
///
/// <para>
/// These scenarios drive a real browser against a running Vanadium stack, so they are opt-in:
/// the whole fixture self-ignores unless <c>VANADIUM_E2E_BASEURL</c> is set. That keeps the
/// default <c>dotnet test Vanadium.slnx</c> pass green in environments without browsers or a live
/// app. To actually run them, start the backend + frontend, install browsers once
/// (<c>pwsh bin/Debug/net10.0/playwright.ps1 install chromium</c>), and set the env vars described
/// in <c>README.md</c>.
/// </para>
/// </summary>
[TestFixture]
public abstract class PlaywrightScenarioBase
{
    protected string BaseUrl { get; private set; } = string.Empty;
    protected string Password { get; private set; } = string.Empty;
    private IPlaywright? _playwright;
    protected IBrowser Browser { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task GlobalSetupAsync()
    {
        BaseUrl = Environment.GetEnvironmentVariable("VANADIUM_E2E_BASEURL") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            Assert.Ignore(
                "VANADIUM_E2E_BASEURL is not set — Playwright scenarios require a running Vanadium " +
                "stack and installed browsers. See Vanadium.Note.Web.E2E/README.md.");
        }

        Password = Environment.GetEnvironmentVariable("VANADIUM_E2E_PASSWORD") ?? string.Empty;

        _playwright = await Playwright.CreateAsync();
        var headed = Environment.GetEnvironmentVariable("VANADIUM_E2E_HEADED") == "1";
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
        });
    }

    [OneTimeTearDown]
    public async Task GlobalTeardownAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
    }

    /// <summary>Opens a fresh, isolated browser context pinned to the app's base URL.</summary>
    protected Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });

    /// <summary>Logs the given page into the app via the password-only login form.</summary>
    protected async Task LoginAsync(IPage page)
    {
        await page.GotoAsync("/login");
        await page.FillAsync("#password", Password);
        await page.ClickAsync(".login-btn");
        // Landing on Home clears /login from the URL.
        await page.WaitForURLAsync(u => !u.Contains("/login"), new() { Timeout = 15_000 });
    }
}
