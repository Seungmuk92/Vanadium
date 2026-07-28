# Vanadium.Note.Web.E2E — Playwright worst-path scenarios

End-to-end scenarios (issue #308) that drive a **real browser** against a **running Vanadium
stack**. They cover the three front-end worst paths that unit/bUnit tests cannot reach end to end:

1. **`SaveDuringExpiryScenarioTests`** — saving a note while the JWT expires mid-session must clear
   the token and redirect to `/login` with a `returnUrl` (issues #117 / #297).
2. **`TwoTabConflictScenarioTests`** — two tabs editing the same note: the stale save gets a 409
   conflict banner with Force Save (issue #221), never a silent clobber.
3. **`KoreanMentionImeScenarioTests`** — mentioning a note by a Hangul title through an IME commit
   (`input` events, not per-key `keydown`s) must still search, match, and insert the mention.

## Why these do not run in the normal test pass

`PlaywrightScenarioBase` **self-ignores the whole fixture** unless `VANADIUM_E2E_BASEURL` is set, so
`dotnet test Vanadium.slnx` stays green in CI / dev boxes that have no browsers or no running app.
The scenarios execute (and can pass) only when you point them at a live stack, as below.

## Running the scenarios

1. Start the backend and frontend (see the repo `CLAUDE.md`):

   ```bash
   cd Vanadium.Note.REST && dotnet run      # https://localhost:7711
   cd Vanadium.Note.Web  && dotnet run      # https://localhost:7700
   ```

2. Install the Playwright browsers once (after a build of this project):

   ```bash
   pwsh Vanadium.Note.Web.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

3. Set the environment variables and run:

   ```bash
   # PowerShell
   $env:VANADIUM_E2E_BASEURL = "https://localhost:7700"
   $env:VANADIUM_E2E_PASSWORD = "<the dev login password>"
   $env:VANADIUM_E2E_HEADED  = "1"   # optional: run headed to watch the browser
   dotnet test Vanadium.Note.Web.E2E
   ```

| Variable | Required | Purpose |
|---|---|---|
| `VANADIUM_E2E_BASEURL` | yes | Base URL of the running frontend; unset ⇒ all scenarios ignored |
| `VANADIUM_E2E_PASSWORD` | yes (to log in) | Dev login password |
| `VANADIUM_E2E_HEADED` | no | `1` runs the browser headed instead of headless |
