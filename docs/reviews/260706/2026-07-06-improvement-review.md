# Vanadium Improvement Review — 2026-07-06

Full-codebase review covering security, usability, features, and code quality.
Severity: **Critical / High / Medium / Low**. File references verified against current code.

## Executive summary

The codebase is in good shape for a personal project: auth is carefully built (timing-safe compare, magic-byte upload validation, LIKE-wildcard escaping, no missing `[Authorize]`), frontend error handling is disciplined (`ServiceResult` + Snackbar everywhere), and optimistic concurrency has real UI. The important gaps cluster in four places: **data-loss edge cases** (over-posting on create, orphan-cleanup race, missing uploads volume, SubNoteDialog discarding pending edits), **XSS surface** (no server-side HTML sanitization + three `innerHTML` sinks with a JWT in localStorage), **resilience** (CDN-loaded editor, 401 mid-session losing work), and **test coverage** (nothing security-relevant is tested).

## High-priority findings

### H1. Over-posting on note create can birth invisible, purge-doomed notes — High
`NotesController.Create` binds the `NoteItem` entity directly; `NoteService.Create` (NoteService.cs:203) only overwrites `Id`, `UpdatedAt`, `ContentText`. A request body with `"deletedAt": "..."` creates a note that is soft-deleted with `IsDeletionRoot = false` — hidden everywhere including the Recycle Bin list, unrestorable, and silently purged by `RecycleBinPurgeJob`. Same for `ArchivedAt`.
**Fix:** in `Create`, force `DeletedAt = null; IsDeletionRoot = false; ArchivedAt = null; IsArchiveRoot = false;` (or introduce a create DTO). One-line class of fix.

### H2. No server-side HTML sanitization → PAT-to-session privilege escalation — High
Note HTML is stored verbatim; `StripHtml` only feeds `ContentText`. A leaked API token can write `<script>` into a note that runs in the owner's browser, exfiltrates the JWT from localStorage, and reaches `DELETE /api/settings/all-data`. Client-side Tiptap schema filtering is the only defense.
**Fix:** sanitize on save with an allowlist matching the Tiptap schema (e.g. `HtmlSanitizer` — a justified new dependency), or at minimum document the PAT trust model.

### H3. `innerHTML` XSS sinks in tiptap-editor.js — High
Mention menu (line 480, interpolates note titles), upload toast (line 1453, filename), Mermaid error preview (line ~700, error message). A title like `<img src=x onerror=...>` executes; combined with H2 these payloads persist.
**Fix:** replace with `textContent`/`createElement` (~15 lines).

### H4. SubNoteDialog silently discards edits from the last 1.5 s — High
`Close()` and `Dispose()` cancel the debounce CTS without flushing (unlike `NoteEditor.DisposeAsync` and `ExpandToFullPage`, which do). Closing via button/Esc/backdrop drops pending content while status still shows "Unsaved changes".
**Fix:** `await DoAutoSave()` in `Close()` when a save is pending.

### H5. Ctrl+K is double-bound: link popover + quick-nav both fire — High
tiptap-editor.js:1768 opens the link popover on Ctrl/Cmd+K with `preventDefault` but no `stopPropagation`; the event bubbles to the global `keyboard-shortcuts.js` handler registered in `MainLayout`, so the link popover opens underneath the quick-nav overlay.
**Fix:** `e.stopPropagation()` in the editor listener; consider moving link to Ctrl+Shift+K.

### H6. Orphan-file cleanup can delete freshly uploaded files — High
`FileCleanupService.DeleteAllOrphansAsync` treats any unreferenced file as orphaned with no grace period. A file uploaded but not yet saved into note HTML (auto-save is 1500 ms; the user may still be composing) is deleted if the scan fires. `FileAttachment.UploadedAt` exists but is never consulted.
**Fix:** skip attachments with `UploadedAt` (and image files with `CreationTimeUtc`) inside a ~1 h grace window.

### H7. Uploads are lost on container recreation — High
`UploadsPath` lives in the container filesystem and docker-compose.yml defines no volume for the REST service. Every image update destroys all uploaded files while `FileAttachments` rows survive (downloads 404).
**Fix:** add a named volume for `/app/uploads`. Also: add a `USER` directive to the Dockerfile (currently runs as root) and pin the image tag.

### H8. No unsaved-changes protection on tab close — High
No `beforeunload` handler exists in app JS. `NoteEditor.DisposeAsync` covers in-app navigation only; closing the tab within the debounce window loses data silently.
**Fix:** register/unregister `beforeunload` while `_hasPendingChanges` is true.

## Medium-priority findings

### Security / backend
- **M1. JwtSecret not validated at startup** — `""` (the appsettings default) passes the null check and fails later with an opaque error; short secrets weaken HS256. Validate `Length >= 32` at startup (Program.cs:56).
- **M2. PBKDF2 100k iterations below current OWASP guidance (600k)** — raising requires re-hashing; consider encoding the iteration count into the stored format (`salt:hash:iterations`).
- **M3. Login rate limiter is one global bucket** — anyone can lock the owner out with 10 junk req/min. Partition by client IP (requires ForwardedHeaders, see M4).
- **M4. No HTTPS redirect / HSTS / ForwardedHeaders / security headers** — if a TLS proxy is assumed, add `UseForwardedHeaders` so IP/scheme are correct; add `X-Content-Type-Options: nosniff` at minimum (relevant to file serving).
- **M5. `ImagesController.Get` sets `Cache-Control: public` on an authorized endpoint** — a shared proxy could cache private images. Use `ResponseCacheLocation.Client`.
- **M6. Mention stripping misses recycle-bin notes** — `StripMentionReferencesAsync` and `UpdatePageLinkReferences` query through the `DeletedAt == null` filter, violating the project's own `IgnoreQueryFilters()` rule; restored notes carry dead mention links and stale page-link titles.
- **M7. `HardDeleteAsync` multi-save sequence runs without a transaction** — partial completion possible on failure; wrap in execution strategy + transaction like `AccountService`.

### Frontend / UX
- **M8. 401 mid-session loses work and context** — `AuthTokenHandler` redirects to `/login` without `returnUrl`; navigation disposes `NoteEditor` whose flush save re-sends tokenless and fails. Append `returnUrl`, stash pending content in `sessionStorage`.
- **M9. Login collapses 401/429/network into "Invalid password"** — a rate-limited or offline user is told their password is wrong. Return a discriminated result.
- **M10. Any note-load failure is treated as 404** — `NoteEditor` prunes recents and redirects home on transient 500s/network blips. Make `GetAsync` status-aware; only prune on real 404.
- **M11. Manual Save races auto-save** — `SaveNote()` bypasses the `_isSaving` guard; Ctrl+S during an in-flight auto-save produces a false 409 conflict banner. Also the cosmetic 2 s "Saved" delay holds `_isSaving` and blocks the next save.
- **M12. Editor stack loaded from esm.sh/jsdelivr at runtime, partially unpinned** — `prosemirror-state`, `prosemirror-view`, `lowlight`, `tiptap-markdown` are version-unpinned. App is unusable offline or on CDN failure, upstream publishes can break the editor overnight, and init failure only logs to console (dead editor div, no user-facing message). Pin versions now; bundle locally longer-term; show a "Editor failed to load — Retry" banner.
- **M13. Keyboard accessibility gaps** — Home note rows, sort headers, expand buttons are click-only `div`s; QuickNavDialog lacks `role="dialog"`/`aria-modal`; Board dialog has no focus trap. Use anchors for titles, buttons for headers, aria attributes on the palette.
- **M14. Inconsistent confirmation UX** — native `confirm()` (Home, NoteEditor) vs styled `ConfirmDialog` (Archive, RecycleBin); `NoteEditor.DeleteNote` with no children shows no confirmation while Home always confirms the same action. Extract a shared confirm service.
- **M15. Component duplication** — pagination markup ×3, `ConfirmAsync` ×2, ~55-line label picker duplicated between NoteEditor and SubNoteDialog, and auto-save plumbing duplicated with divergent quality (SubNoteDialog uses fire-and-forget `ContinueWith`, no reentrancy guard). Extract `Pagination`, `LabelPicker`, confirm helper; unify auto-save into a shared helper.
- **M16. No mobile/responsive support** — no page-level media queries; table, board, editor header overflow on narrow screens. Fine if desktop-only is deliberate — document it in CLAUDE.md.
- **M17. Stale token captured by editor closures** — token fetched once at `tiptapInterop.init`; after expiry + re-login, open editors fail image loads/uploads silently. Fetch token per operation via `dotnetRef`.

## Low-priority findings

- **L1.** Error contract inconsistency: mixture of `ProblemDetails`, `{ message }`, `{ error }`, bare-string `BadRequest`. Standardize on `ProblemDetails`.
- **L2.** Home shows "No notes yet. Create one!" after a failed load; Board has no loading state. Add failure/loading states.
- **L3.** Search placeholder says "Search by title..." but search covers title + content.
- **L4.** Concurrency check is read-then-write in memory; `[ConcurrencyCheck]` on `UpdatedAt` would make it airtight.
- **L5.** `async void` auth-state handlers in MainLayout/NavMenu — wrap in try/catch.
- **L6.** tiptap-editor.js is a 2064-line monolith; already an ES module — split into `nodes/*.js`, `upload.js`, `interop.js` without a build step.
- **L7.** Mention suggestions hit the API on every keystroke — add ~150 ms debounce.
- **L8.** `TokenStore` cache never re-reads across tabs; a `storage` listener fixes cross-tab logout.
- **L9.** Unbounded string columns (`FileAttachment.OriginalName`/`ContentType`, `ApiToken.TokenHash`) — add `[MaxLength]`.
- **L10.** `FileCleanupService` loads all note content into one string — fine today; per-GUID `AnyAsync(Contains)` scales better.
- **L11.** Dead code: unreachable content-type fallback in `FilesController.Upload` (~line 74).
- **L12.** Console noise (`console.log`) in production JS paths — use `console.debug`.
- **L13.** CLAUDE.md drift: documented middleware order differs from actual (actual is correct); required docker env vars omit `AUTH_PASSWORD_HASH`; image storage format (`{guid}.{ext}`, no DB record) undocumented.
- **L14.** `CancellationToken` plumbing inconsistent — present in ApiToken/cleanup paths, absent in most NoteService/controller actions.

## Test coverage

83 tests cover only Archive, QuickNav, and Toggle content. **Zero coverage** for: recycle-bin lifecycle (the most state-heavy feature), password hashing/verification, `PasswordValidator`, `ApiTokenService`/`ApiTokenAuthHandler`, label category mutual exclusion, `FileCleanupService`, `AccountService` wipe, and the H1 over-posting path. Everything security-relevant is untested. Suggested first additions: recycle-bin soft-delete → restore → purge round-trip, `Create` field-forcing regression test (after fixing H1), auth hash round-trip.

## Feature gaps worth considering (verified absent)

Backlinks panel ("what links here" — mention data exists but has no UI/endpoint), full-note-set export (only per-note Markdown export exists), .md file import, note templates, drag-to-re-parent in the Home list (API supports `ParentNoteId`), offline/PWA (blocked by M12 until the editor is bundled locally). Already present, so not re-proposed: dark mode, markdown paste/export, code highlighting, board drag-drop, command palette, toggles/accordions.

## Suggested order of attack

1. **Data-loss fixes (small, high value):** H1 (force-null fields in `Create`), H4 (flush save on dialog close), H6 (cleanup grace period), H7 (uploads volume), H8 (`beforeunload`).
2. **XSS surface:** H3 (`textContent`, ~15 lines), then decide on H2 (server-side sanitizer vs documented trust model).
3. **Resilience:** M12 (pin CDN versions now, bundle later), M8–M10 (401/load-failure handling), H5 (Ctrl+K).
4. **Hygiene:** M1 (JwtSecret validation), M5 (cache header), M6 (mention stripping filter), then tests for the recycle bin and auth.
