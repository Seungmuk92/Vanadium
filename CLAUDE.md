# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Goal

Vanadium is a personal project aimed at building a Notion-like note-taking app tailored for developers. The ultimate goal is a custom note app with a developer-friendly workflow.

## Code Conventions

- All code, comments, and UI text must be written in English. Korean must not appear anywhere in the codebase.
- C# nullable reference types are enabled — never disable with `#nullable disable`; handle nulls explicitly.
- File-per-class. Namespace must match folder structure.
- Public DTOs/contracts: PascalCase properties, no abbreviations.
- Avoid introducing new top-level dependencies without justification — this is a personal project and dependency surface is intentionally small.
- Prefer async/await end-to-end for I/O paths (EF Core, HttpClient, file I/O).

## Overview

Vanadium is a personal note-taking app consisting of two .NET 10 projects:

- **`Vanadium.Note.REST`** — ASP.NET Core Web API backend (JWT auth, PostgreSQL via EF Core)
- **`Vanadium.Note.Web`** — Blazor WebAssembly frontend (MudBlazor UI, Tiptap rich text editor via JS interop)

## Commands

### Run locally

```bash
# Backend (starts on https://localhost:7711 by default)
cd Vanadium.Note.REST
dotnet run

# Frontend (starts on https://localhost:7700 / http://localhost:7700)
cd Vanadium.Note.Web
dotnet run
```

### Build

```bash
dotnet build Vanadium.slnx
```

### EF Core migrations

```bash
# Add a migration (run from solution root)
dotnet ef migrations add <MigrationName> --project Vanadium.Note.REST

# Apply migrations (also runs automatically on startup)
dotnet ef database update --project Vanadium.Note.REST
```

### Docker (production)

```bash
docker compose up -d
```

Required env vars for `docker-compose.yml`: `DB_PASSWORD`, `AUTH_JWT_SECRET`, `AUTH_PASSWORD_HASH`. Optional: `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `CORS_ALLOWED_ORIGINS`, `API_BASE_URL`.

### Verifying a change

Before reporting work as done:

1. `dotnet build Vanadium.slnx` — must compile clean (no new warnings introduced)
2. If schema changed: `dotnet ef database update --project Vanadium.Note.REST` against a dev DB
3. For backend changes affecting controllers: run `dotnet run` in `Vanadium.Note.REST` and hit Swagger at `/swagger`
4. For frontend changes: run both projects and exercise the affected page

## Architecture

### Authentication flow

Single-user, **password-only** (no user identity). There is no `User` entity and no `UserId`/`Username` ownership column on any table — the whole database belongs to one owner. Login verifies a password against a configured hash; a successful login mints a JWT carrying only a fixed `ClaimTypes.Name` = `AuthController.OwnerName` (`"owner"`) claim.

- `POST /api/auth/login` (body `{ password }`) verifies against `Auth:PasswordHash` (PBKDF2-SHA256, `base64salt:base64hash:iterations` — supplied via config/env, never stored in the DB; legacy two-part `base64salt:base64hash` values verify at 100k for backward compatibility) and returns a JWT; no refresh tokens — JWT expiry defaults to 1440 min.
- `POST /api/auth/hash` (dev-only, body `{ password }`) returns the storage hash for a password so it can be pasted into `Auth:PasswordHash`. It persists nothing (replaces the old user-provisioning `setup` endpoint).
- Personal access tokens (`ApiToken`) still work; they no longer carry a `UserId`. `ApiTokenAuthHandler` matches the token hash and emits the same single `Name` claim.
- Frontend stores JWT in `TokenStore` (scoped service wrapping `localStorage`), injects it via `AuthTokenHandler` (delegating `HttpMessageHandler`).
- `JwtAuthenticationStateProvider` parses the JWT client-side to expose auth state to Blazor.
- Because ownership scoping is gone, service methods take no `userId` and run no `.Where(x => x.UserId == …)` filter; `UserSettings` is a singleton row. Migration `20260702000000_RemoveUserConcept` drops the `Users` table, the `UserId` columns, and `UserSettings.Username`.

### Note content

Notes store HTML produced by a **Tiptap** editor. JS interop is handled exclusively in `NoteEditor.razor` via `tiptapInterop.*` calls (`init`, `setContent`, `focus`, `destroy`). Each editor instance is keyed by a unique DOM id (`tiptap-{guid}`).

Auto-save fires 1500 ms after the last content/title change via a debounced `CancellationTokenSource` pattern.

Custom editor nodes (all in `wwwroot/js/tiptap-editor.js`, serialized as `div[data-type=...]`): `Callout`, `MermaidNode`, `PageLink`, `NoteMention`, `FileAttachment`, plus the collapsible trio `Toggle`/`ToggleSummary`/`ToggleContent` and `AccordionGroup`. **Serialization hard rule: user-visible text must live in element text content, never in attribute values** — the server derives `ContentText` via `StripHtml` (tag → space) for trigram search, and attribute values are discarded.

### Collapsible content (toggles)

Editor-layer-only feature (no backend/schema change): toggle blocks (`/toggle`), collapsible headings (`/toggleh1`–`/toggleh3`; the StarterKit `Heading` is extended with `data-collapsible`/`data-open` attrs — NOT a new node, registered as `CollapsibleHeading` with `StarterKit.configure({ heading: false })`), and accordion groups (`/accordion`, at most one toggle open per group, enforced by an `appendTransaction` plugin). Folding is CSS-only off `data-open` — collapsed bodies stay in the document HTML, so `ContentText` search and `OrphanFileCleanupJob` scanning are unaffected. Fold state persists with the note and rides the normal auto-save; fold transactions carry `addToHistory: false` so undo never replays them. Fold/unfold also works in read-only (archived) mode, in-memory only. Keymap: `Enter` in summary → body; `Mod-Enter` → fold/unfold; `Backspace` in empty summary → unwrap; `Enter` on trailing empty body paragraph → exit below the toggle. Full design: `docs/plannings/note-toggle-feature.md`.

### File uploads

Two upload kinds share the `Vanadium.Note.REST/uploads/` directory:

- **File attachments** (`FilesController`) — stored on disk as `file_{guid}` (no extension), with a metadata row in `FileAttachments`. The upload response returns `url = /api/files/{guid}`, and that is the reference form embedded in note HTML.
- **Images** (`ImagesController`) — stored on disk as `{guid}{ext}` (extension preserved), with **no** DB record. The upload response returns `url = /api/images/{guid}`.

An `OrphanFileCleanupJob` (background hosted service) periodically removes files whose GUIDs no longer appear in any note's HTML content — `FileCleanupService` scans note HTML for `/api/files/{guid}` and `/api/images/{guid}` references. Soft-deleted notes' content still counts as a live reference (the scans use `IgnoreQueryFilters()`).

### Note recycle bin (soft delete)

`DELETE /api/notes/{id}` is a SOFT delete: it sets `NoteItem.DeletedAt` (+ `IsDeletionRoot` on the directly-deleted note) on the note and its active descendants. Soft-deleted notes are hidden by a **global query filter** (`DeletedAt == null`) on `NoteItem` (and a matching filter on `NoteLabel`). Recycle Bin-aware paths use `IgnoreQueryFilters()`: recycle bin list/restore/permanent delete in `NoteService`, `RecycleBinPurgeJob`, both content scans in `FileCleanupService`, and the account wipe in `AccountService`. **Any new code that scans note content or bulk-deletes notes must consider whether it needs `IgnoreQueryFilters()`** — otherwise soft-deleted notes' files get garbage-collected early or soft-deleted notes survive deletion.

Recycle bin endpoints: `GET /api/notes/recycle-bin`, `POST /api/notes/{id}/restore`, `DELETE /api/notes/{id}/permanent` (409 on active notes), `DELETE /api/notes/recycle-bin` (empty). `RecycleBinPurgeJob` (hosted service) permanently deletes notes soft-deleted longer than `RecycleBin:RetentionDays` (default 30). Reference stripping and file cleanup happen at PERMANENT delete time, not at recycle bin time, so restores are lossless. Full design: `docs/recycle-bin-feature.md`.

### Note archive

A third note state between active and deleted: `NoteItem.ArchivedAt` (+ `IsArchiveRoot` on the directly-archived note). Archived notes are kept forever (no purge job by design), hidden from Home/Board/children/mention search, included in full-text search (`NoteSummary.IsArchived` drives the UI badge), retrievable via `GET /api/notes/{id}`, and READ-ONLY: `PUT /api/notes/{id}` and label add/remove return 403 (`ProblemDetails`), checked before the optimistic-concurrency check. Creating or re-parenting under an archived note is rejected with 400. Archiving sweeps active descendants into a shared-`ArchivedAt` group (mirrors `DeletedAt` groups); unarchive restores the same group and re-attaches to root if the original parent is missing, soft-deleted, or still archived.

**Archive deliberately has NO global query filter.** EF Core allows one `HasQueryFilter` per entity, archive visibility is not uniform (visible in search/GET-by-id/archive page, hidden elsewhere), and keeping archived notes visible to default queries means the `FileCleanupService` scans and `AccountService` wipe see archived content with zero changes. Archived-note exclusion is done with explicit `Where(n => n.ArchivedAt == null)` predicates on exactly the reads that need it (`GetPaged` non-search branch, `GetAllSummaries`, `GetChildren`, `SearchForMention`). Do not fold `ArchivedAt` into the global filter — every existing `IgnoreQueryFilters()` opt-out would silently change meaning.

Archive ↔ recycle bin: `DELETE /api/notes/{id}` accepts archived notes and keeps `ArchivedAt`, so a recycle-bin restore returns the note to the Archive, not the active list. `DELETE /api/notes/{id}/permanent` still 409s for archived-but-not-deleted notes (no destruction shortcut).

Archive endpoints: `POST /api/notes/{id}/archive` (idempotent, 204/404), `POST /api/notes/{id}/unarchive` (204/404), `GET /api/notes/archive?page=&pageSize=` (paged archive roots, `ArchivedAt` desc). Frontend: `/archive` page, read-only editor mode (`tiptapInterop.init` `editable` flag / `tiptapInterop.setEditable`), archive row action + Undo on Home. Full design: `docs/plannings/note-archive-feature.md`.

### Label system

Labels have an optional `LabelCategory`. Within a category, labels are mutually exclusive on a note (adding one removes others in the same category). The many-to-many `NoteLabel` join table has a composite PK `(NoteId, LabelId)`.

`NoteItem.Labels` (`[NotMapped]`) is populated from `NoteLabels` navigation property by `NoteService.PopulateLabels()` — it is never persisted directly.

### Frontend pages

| Route | Component |
|---|---|
| `/` | `Home.razor` — note list |
| `/board` | `Board.razor` — kanban-style board view |
| `/editor` | `NoteEditor.razor` — new note |
| `/editor/{id}` | `NoteEditor.razor` — edit existing note (read-only when archived) |
| `/archive` | `Archive.razor` — archive list (unarchive / delete to recycle bin) |
| `/recycle-bin` | `RecycleBin.razor` — recycle bin list (restore / delete forever / empty) |
| `/login` | `Login.razor` |

### Web deployment

The Blazor WASM static files are served via nginx. `API_BASE_URL` is injected at container startup by `entrypoint.sh`, which uses `envsubst` to rewrite `appsettings.json` from the template baked into the image.

### Development configuration

`Vanadium.Note.REST/appsettings.Development.json` contains a live dev database connection and JWT secret — these are intentionally committed for local development convenience.

The frontend's dev API base URL is set in `Vanadium.Note.Web/wwwroot/appsettings.Development.json` (`ApiBaseUrl`).

### Middleware pipeline (REST)

Order matters. Registered in `Program.cs`:

1. `UseForwardedHeaders` — restores client IP / request scheme from the reverse proxy
2. `UseHsts` — non-Development only
3. `SecurityHeadersMiddleware` — security response headers
4. `CorrelationIdMiddleware` — assigns/propagates `X-Correlation-Id`
5. `UseCors`
6. `UseRateLimiter`
7. `UseAuthentication`
8. `UserContextMiddleware` — pushes username into Serilog `LogContext`
9. `RequestLoggingMiddleware` — structured request log
10. `UseAuthorization`
11. `MapControllers`

When adding new middleware, place it AFTER `CorrelationIdMiddleware` so logs carry the correlation ID.

### Logging

- Serilog is the only logger — do not use raw `Console.WriteLine` for production paths.
- Use structured templates: `_logger.LogInformation("Note {NoteId} updated by {UserId}", noteId, userId);` — never string-concatenate.
- Correlation ID and Username are auto-enriched via middleware; do not push them manually.
- Optional Seq sink activates only when both `Seq:ServerUrl` and `Seq:ApiKey` are configured.

### Rate limiting & security

- `/api/auth/login` is fixed-window rate-limited at 10 req/min. Do not remove without discussion.
- Passwords are hashed with PBKDF2-SHA256 in `Security/PasswordHasher.cs`; new hashes use 600k iterations (OWASP guidance) and encode the count in the storage format (`salt:hash:iterations`) so it can be raised without rehashing. Legacy two-part hashes verify at 100k. Never weaken these parameters.
- JWT validation does NOT check issuer/audience by design (single-tenant). Keep it that way unless deployment changes.
- File uploads enforce a 13-MIME whitelist and 100 MB cap; new types must be added explicitly to both the controller and any frontend validation.

### Full-text search

`NoteItem.Title` + `ContentText` are indexed with a PostgreSQL GIN **trigram** index (`gin_trgm_ops`, since migration `20260426140544_SwitchToTrigramSearch`). `NoteService.ApplyFilters()` applies `EF.Functions.ILike` per whitespace-separated term, which the trigram index accelerates. When adding searchable fields, extend both the index and the search query — never `ILIKE` a non-indexed column for performance reasons. Search results include archived notes (flagged `IsArchived`); the non-search Home list excludes them.

### Note sharing

A note can be exposed to anonymous readers via an unguessable share token (issue #94). `NoteItem` carries three **server-owned** fields — `ShareToken` (nullable, partial-unique index), `ShareMode` (`None`/`Public`/`Link`), `SharedAt` — that are forced to the not-shared state in `NoteService.Create` and never copied from a client payload in `Update`, so a create/update can never mint or tamper with a token. Owner endpoints live on `NotesController` (`GET`/`PUT`/`DELETE /api/notes/{id}/share`); the single anonymous read path is `GET /api/share/{token}` on `ShareController` (`[AllowAnonymous]`, rate-limited per IP via the `share` policy), returning a lean read-only `SharedNote` (title + sanitized HTML only — never the token). `GetSharedByToken` uses the **default-filtered** `db.Notes`, so a soft-deleted note's link stops resolving; unsharing clears the token, which immediately and permanently invalidates the old link (re-sharing mints a fresh token). `Link` mode adds `X-Robots-Tag: noindex`; `Public` does not. Frontend: a `Share` button + `ShareDialog` in the editor, and the anonymous `/share/{token}` page (`Share.razor`, `[AllowAnonymous]`, minimal `ShareLayout`). **Known limitation:** embedded images/attachments reference authenticated endpoints, so they may not render for anonymous viewers (an anonymous asset proxy is deliberately out of scope).

### Quick Navigation (command palette)

A keyboard-first note switcher opened by `Ctrl+K` (Windows/Linux) / `Cmd+K` (macOS) from any authenticated page. It is an ephemeral overlay (`Components/QuickNavDialog.razor`, hosted once in `MainLayout.razor`), NOT a route — empty input shows recent notes, typing runs live search. Full design: `docs/plannings/note-quick-navigation-feature.md`.

- **Backend:** `GET /api/notes/quick-search?q=&limit=` → `NoteService.QuickSearch` → lean `QuickNavResult` (id, title, snippet, `IsArchived`). It **reuses the same trigram `EF.Functions.ILike` path** as `ApplyFilters` and deliberately **includes archived notes** (no `ArchivedAt == null` predicate) while the default `DeletedAt == null` global filter excludes Recycle Bin notes — so it uses the **default-filtered** `db.Notes` set with **no `IgnoreQueryFilters()`** (mirrors `GetPaged`'s search branch). Ordered by `UpdatedAt` desc, `limit` clamped `[1,50]`. `BuildSnippet` computes a ≤160-char plain-text preview in memory off the tag-stripped `ContentText`. No schema change, no migration.
- **Recents** are stored **client-side only** in `localStorage` (key `vanadium.recents.v1`, via `wwwroot/js/quick-nav.js` + `QuickNavService`) — no `NoteVisit` table, no endpoint. A visit is recorded in `NoteEditor.razor` after a successful note load (covers every path that lands on `/editor/{id}`); stale entries that 404 on open are pruned lazily.
- **Shortcut infra:** registers the `ctrl+k` chord through the existing `KeyboardShortcutService`; `keyboard-shortcuts.js` already maps `metaKey`→`ctrl` and fires while inputs are focused (modifier present), so no JS change was needed. `Esc`/arrows/`Enter` are handled inside the palette, not globally.

## Known limitations

- **Tests:** `Vanadium.Note.REST.Tests` (xUnit + EF Core SQLite in-memory) covers `NoteService`-level logic. PostgreSQL-only behavior (trigram `ILike` search) is out of unit scope and must be verified manually. Run with `dotnet test Vanadium.slnx`. The Web project has no tests.
- **DTOs are duplicated** between REST and Web projects on purpose (kept simple). Do not introduce a shared `Vanadium.Note.Shared` project unless explicitly requested.
- **No CI workflow** is set up. Build/lint must be verified locally with `dotnet build Vanadium.slnx`.
- **No refresh tokens.** When the JWT expires, the user re-logs in. Don't propose refresh-token implementations without discussion.
- **`appsettings.Development.json` is committed intentionally** with a real dev DB connection and JWT secret. Do not move these values to user-secrets without checking first.

## When making changes

- **Schema changes:** always create an EF Core migration (`dotnet ef migrations add <Name> --project Vanadium.Note.REST`). Never edit existing migration files.
- **New API endpoint:** add (1) DTO in `Vanadium.Note.REST/Models`, (2) DTO mirror in `Vanadium.Note.Web/Models`, (3) HTTP client method in `NoteService`/`LabelService`/etc.
- **Tiptap editor changes:** all JS interop must go through `tiptapInterop.*` in `wwwroot/js/tiptap-editor.js` and be invoked only from `NoteEditor.razor`. Do not call interop from other components.
- **File uploads:** orphan cleanup expects file references to appear as `/api/files/{guid}` (attachments) and `/api/images/{guid}` (images) substrings in note HTML. Changing either URL format requires updating `FileCleanupService`'s scan patterns.
- **Label categories:** mutual exclusion within a category is enforced server-side in `NoteService`, not via DB constraint. Frontend should not assume DB will reject duplicates.
