# Improvement Execution Plan — 2026-07-06

Source: `2026-07-06-improvement-review.md`. This plan sequences the review's findings into
buildable stages, each with concrete files, changes, tests, and a verification gate.

## Guiding principles

1. **Data-loss first, refactoring last.** Ship the small, high-value bug fixes before any
   structural cleanup (M15 dedup, L6 file split). Never mix a refactor into a bugfix commit —
   it makes review and rollback harder.
2. **Every bugfix ships with its regression test.** The biggest standing risk is that
   "everything security-relevant is untested." Fix → test in the same PR.
3. **One finding (or tight group) per commit.** Run the CLAUDE.md "Verifying a change"
   checklist at each gate: `dotnet build Vanadium.slnx` clean, migrations applied if schema
   changed, Swagger/page exercised for the affected surface.
4. **Decision locked:** H2 will use a server-side `HtmlSanitizer` allowlist (not just a
   documented trust model).

Branch strategy: one short-lived branch per stage (`fix/stage1-data-loss`, …), merged after
its gate passes.

---

## Stage 1 — Data-loss fixes (highest value, small diffs)

Target: eliminate the paths that silently destroy user data. Land these first.

### H1 — Over-posting on note create
- **Files:** `Vanadium.Note.REST/Controllers/NotesController.cs:86` (`Create([FromBody] NoteItem note)`),
  `Vanadium.Note.REST/Services/NoteService.cs:203` (`Create`).
- **Change:** In `NoteService.Create`, force the state fields regardless of request body:
  `note.DeletedAt = null; note.IsDeletionRoot = false; note.ArchivedAt = null; note.IsArchiveRoot = false;`
  (alongside the existing `Id`/`UpdatedAt`/`ContentText` overwrites). Also null the concurrency /
  server-owned fields not meant to be client-set. Prefer this over a create DTO for now (smaller
  surface; DTO is a later hygiene item if over-posting recurs on `Update`).
- **Test:** `NoteServiceTests` — POST body with `DeletedAt`/`ArchivedAt` set produces a note that
  is active and appears in the default list (field-forcing regression).
- **Verify:** build; unit test green.

### H4 — SubNoteDialog discards last 1.5 s of edits
- **File:** `Vanadium.Note.Web` → `Components/SubNoteDialog.razor` (`Close()`, `Dispose()`).
- **Change:** Mirror `NoteEditor.DisposeAsync`/`ExpandToFullPage`: when a save is pending,
  `await DoAutoSave()` before cancelling the debounce CTS. Ensure "Unsaved changes" status is
  cleared only after the flush completes.
- **Test:** Manual — open sub-note, type, close via button/Esc/backdrop within 1.5 s, reopen,
  confirm content persisted. (No unit harness for Blazor components.)
- **Verify:** run both projects, exercise SubNoteDialog.

### H6 — Orphan cleanup deletes freshly uploaded files
- **File:** `Vanadium.Note.REST/Services/FileCleanupService.cs:66` (`DeleteAllOrphansAsync`).
- **Change:** Introduce a grace window (~1 h). Skip `FileAttachment` rows whose `UploadedAt` is
  within the window; for loose image files, skip when `CreationTimeUtc` is within the window.
  Make the window configurable (`FileCleanup:GraceMinutes`, default 60).
- **Test:** `FileCleanupServiceTests` (new) — an unreferenced attachment with recent `UploadedAt`
  survives a scan; an old one is removed.
- **Verify:** build; unit test green.

### H8 — No unsaved-changes protection on tab close
- **Files:** `Vanadium.Note.Web/wwwroot/js/` (new small handler or extend existing interop),
  `NoteEditor.razor` (+ `SubNoteDialog.razor`).
- **Change:** Register a `beforeunload` handler while `_hasPendingChanges` is true; unregister on
  save/dispose. Keep it minimal — browser-native confirm dialog is sufficient.
- **Test:** Manual — edit, attempt tab close within debounce window, confirm prompt appears.
- **Verify:** run frontend, exercise.

### H7 — Uploads lost on container recreation
- **Files:** `docker-compose.yml` (rest service, currently no `volumes:`), `Vanadium.Note.REST/Dockerfile`.
- **Change:** Add a named volume mapped to the container's uploads path (`/app/uploads`). Add a
  non-root `USER` directive to the Dockerfile and pin the base image tag. Also update
  `docker-compose.yml` image tag off `:latest` (see L13 doc note).
- **Test:** Manual — `docker compose up`, upload a file, recreate the rest container, confirm the
  file still downloads.
- **Verify:** local compose smoke test.

**Stage 1 gate:** build clean, new unit tests green, manual smoke of H4/H7/H8.

---

## Stage 2 — XSS surface

### H3 — `innerHTML` sinks in tiptap-editor.js
- **File:** `Vanadium.Note.Web/wwwroot/js/tiptap-editor.js`.
- **Sinks (confirmed):** line 181 (slash-menu row — safe, static, but convert for consistency),
  **line 480 (mention row — interpolates note titles, the real risk)**, line 700 (Mermaid error —
  interpolates `err.message`), line 1453 (upload toast — filename).
- **Change:** Replace interpolated `innerHTML` with `createElement` + `textContent`. Static-icon
  markup can stay but user-derived strings (title, filename, error) must go through `textContent`.
- **Test:** Manual — create a note titled `<img src=x onerror=alert(1)>`, open the mention menu,
  confirm no script executes and the title renders as literal text.
- **Verify:** run frontend, exercise mention/upload/Mermaid-error paths.

### H2 — Server-side HTML sanitization (HtmlSanitizer)
- **Dependency:** add `Ganss.Xss` (HtmlSanitizer) to `Vanadium.Note.REST.csproj` — a justified new
  dependency per the small-surface policy; note the justification in the PR.
- **Files:** `NoteService.Create`/`Update` (sanitize `note.Content` before persisting), plus a
  central `IHtmlSanitizerService` so the allowlist has one owner.
- **Change:** Build an allowlist that mirrors the Tiptap schema — the custom `div[data-type=...]`
  nodes (Callout, MermaidNode, PageLink, NoteMention, FileAttachment, Toggle/ToggleSummary/
  ToggleContent, AccordionGroup), their `data-*` attributes, headings with `data-collapsible`/
  `data-open`, code blocks, links, images pointing at `/uploads/` and `/images/`. **Critical
  constraint:** sanitization must NOT strip the `data-type`/`data-*` attributes the editor and
  `StripHtml`/search depend on — over-tight rules will silently break custom nodes. Enumerate the
  schema from `tiptap-editor.js` before finalizing the allowlist.
- **Test:** `HtmlSanitizerServiceTests` — (a) `<script>` and inline `on*` handlers stripped;
  (b) every custom node's serialized HTML round-trips unchanged; (c) `javascript:` URLs removed.
- **Verify:** build; unit tests green; save each custom node type in the running app and confirm
  it survives a save/reload.

**Stage 2 gate:** build clean, sanitizer tests green (especially node round-trip), manual XSS
payload confirmed inert.

---

## Stage 3 — Resilience

### M12 — Editor loaded from CDN, partially unpinned
- **File:** `tiptap-editor.js` import URLs.
- **Change now:** Pin every unpinned import (`prosemirror-state`, `prosemirror-view`, `lowlight`,
  `tiptap-markdown`) to exact versions. Add a load-failure guard: on init failure, render an
  "Editor failed to load — Retry" banner instead of a dead div + console-only error.
- **Later (backlog):** bundle the editor stack locally (prerequisite for the offline/PWA feature).
- **Verify:** simulate CDN failure (block domain in devtools), confirm banner appears.

### M8 / M9 / M10 — 401 & load-failure handling
- **M8:** `AuthTokenHandler` — append `returnUrl` on redirect to `/login`; stash pending editor
  content in `sessionStorage` before the tokenless flush fails, restore after re-login.
- **M9:** `Login.razor` — return a discriminated result so 401 vs 429 vs network are distinct
  messages (not all "Invalid password").
- **M10:** `NoteEditor.GetAsync` — make status-aware; only prune recents + redirect home on a real
  404, not on 500/network blips.
- **Test:** Manual — expire token mid-edit; hit login rate limit; simulate a 500 on note load.
- **Verify:** run frontend, exercise each path.

### H5 — Ctrl+K double-bound (link popover + quick-nav)
- **File:** `tiptap-editor.js` link-popover keydown listener.
- **Change:** add `e.stopPropagation()` so the event doesn't bubble to the global
  `keyboard-shortcuts.js` handler; optionally move link to Ctrl+Shift+K.
- **Verify:** press Ctrl+K in the editor — only quick-nav opens, no link popover underneath.

**Stage 3 gate:** build clean, manual verification of CDN-failure, 401, and Ctrl+K.

---

## Stage 4 — Hardening & hygiene

Backend security/correctness first, then tests, then cosmetics.

- **M1** — Validate `Auth:JwtSecret` at startup (`Program.cs:56`): reject empty and `Length < 32`
  with a clear fail-fast message.
- **M5** — `ImagesController.Get`: change `Cache-Control: public` to `ResponseCacheLocation.Client`
  (authorized endpoint must not be shared-proxy cacheable).
- **M6** — `StripMentionReferencesAsync` / `UpdatePageLinkReferences`: add `IgnoreQueryFilters()`
  so recycle-bin notes are included (project rule); restored notes keep live mention/page-links.
- **M7** — Wrap `HardDeleteAsync` multi-save sequence in an execution strategy + transaction
  (mirror `AccountService`).
- **M3 / M4** — Add `UseForwardedHeaders`, then partition the login rate limiter by client IP;
  add `X-Content-Type-Options: nosniff` (and consider HSTS/HTTPS redirect if TLS-proxied).
- **M2** — Encode PBKDF2 iteration count into the stored hash format (`salt:hash:iterations`) to
  allow future increases toward OWASP guidance without a breaking format change.
- **Tests to add (priority order):** recycle-bin soft-delete → restore → purge round-trip;
  H1 field-forcing regression (already in Stage 1); auth hash round-trip; `PasswordValidator`;
  `ApiTokenService`/`ApiTokenAuthHandler`; label-category mutual exclusion; `FileCleanupService`
  (Stage 1); `AccountService` wipe.
- **Docs (L13):** fix CLAUDE.md drift — required docker env vars omit `AUTH_PASSWORD_HASH`;
  document image storage format; note M16 desktop-only decision if that's intended.

Remaining M/L items (M11 save race, M13 a11y, M14 confirm UX, M15 dedup, M17 stale token, L1–L14)
are tracked as backlog and pulled in opportunistically — none block the stages above.

---

## Sequencing summary

| Stage | Findings | Theme | Risk if skipped |
|---|---|---|---|
| 1 | H1, H4, H6, H7, H8 | Data loss | Silent, unrecoverable data loss |
| 2 | H3, H2 | XSS | JWT exfiltration → full-data delete |
| 3 | M12, M8–M10, H5 | Resilience | Lost work on 401/CDN/tab close |
| 4 | M1–M7, tests, docs | Hardening | Latent correctness/security debt |

Each stage is independently shippable. Recommended cadence: Stage 1 first (fast, high value),
then Stage 2 before exposing any PAT more widely.
