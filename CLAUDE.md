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

Required env vars for `docker-compose.yml`: `DB_PASSWORD`, `AUTH_JWT_SECRET`. Optional: `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `CORS_ALLOWED_ORIGINS`, `API_BASE_URL`.

### Verifying a change

Before reporting work as done:

1. `dotnet build Vanadium.slnx` — must compile clean (no new warnings introduced)
2. If schema changed: `dotnet ef database update --project Vanadium.Note.REST` against a dev DB
3. For backend changes affecting controllers: run `dotnet run` in `Vanadium.Note.REST` and hit Swagger at `/swagger`
4. For frontend changes: run both projects and exercise the affected page

## Architecture

### Authentication flow

- `POST /api/auth/login` returns a JWT; no refresh tokens — JWT expiry defaults to 1440 min.
- `POST /api/auth/setup` (dev-only) creates/updates users.
- Frontend stores JWT in `TokenStore` (scoped service wrapping `localStorage`), injects it via `AuthTokenHandler` (delegating `HttpMessageHandler`).
- `JwtAuthenticationStateProvider` parses the JWT client-side to expose auth state to Blazor.

### Note content

Notes store HTML produced by a **Tiptap** editor. JS interop is handled exclusively in `NoteEditor.razor` via `tiptapInterop.*` calls (`init`, `setContent`, `focus`, `destroy`). Each editor instance is keyed by a unique DOM id (`tiptap-{guid}`).

Auto-save fires 1500 ms after the last content/title change via a debounced `CancellationTokenSource` pattern.

### File uploads

Files are uploaded to `Vanadium.Note.REST/uploads/` as `file_{guid}`. Metadata is stored in `FileAttachments`. An `OrphanFileCleanupJob` (background hosted service) periodically removes files whose GUIDs no longer appear in any note's HTML content.

### Label system

Labels have an optional `LabelCategory`. Within a category, labels are mutually exclusive on a note (adding one removes others in the same category). The many-to-many `NoteLabel` join table has a composite PK `(NoteId, LabelId)`.

`NoteItem.Labels` (`[NotMapped]`) is populated from `NoteLabels` navigation property by `NoteService.PopulateLabels()` — it is never persisted directly.

### Frontend pages

| Route | Component |
|---|---|
| `/` | `Home.razor` — note list |
| `/board` | `Board.razor` — kanban-style board view |
| `/editor` | `NoteEditor.razor` — new note |
| `/editor/{id}` | `NoteEditor.razor` — edit existing note |
| `/login` | `Login.razor` |

### Web deployment

The Blazor WASM static files are served via nginx. `API_BASE_URL` is injected at container startup by `entrypoint.sh`, which uses `envsubst` to rewrite `appsettings.json` from the template baked into the image.

### Development configuration

`Vanadium.Note.REST/appsettings.Development.json` contains a live dev database connection and JWT secret — these are intentionally committed for local development convenience.

The frontend's dev API base URL is set in `Vanadium.Note.Web/wwwroot/appsettings.Development.json` (`ApiBaseUrl`).

### Middleware pipeline (REST)

Order matters. Registered in `Program.cs`:

1. `CorrelationIdMiddleware` — assigns/propagates `X-Correlation-Id`
2. `UserContextMiddleware` — pushes username into Serilog `LogContext`
3. `RequestLoggingMiddleware` — structured request log
4. CORS, Authentication, Authorization, RateLimiter

When adding new middleware, place it AFTER `CorrelationIdMiddleware` so logs carry the correlation ID.

### Logging

- Serilog is the only logger — do not use raw `Console.WriteLine` for production paths.
- Use structured templates: `_logger.LogInformation("Note {NoteId} updated by {UserId}", noteId, userId);` — never string-concatenate.
- Correlation ID and Username are auto-enriched via middleware; do not push them manually.
- Optional Seq sink activates only when both `Seq:ServerUrl` and `Seq:ApiKey` are configured.

### Rate limiting & security

- `/api/auth/login` is fixed-window rate-limited at 10 req/min. Do not remove without discussion.
- Passwords are hashed with PBKDF2-SHA256, 100k iterations. Never weaken these parameters.
- JWT validation does NOT check issuer/audience by design (single-tenant). Keep it that way unless deployment changes.
- File uploads enforce a 13-MIME whitelist and 100 MB cap; new types must be added explicitly to both the controller and any frontend validation.

### Full-text search

`NoteItem` indexes `Title` + `ContentText` via a PostgreSQL `tsvector` column with a GIN index. Searches go through `NoteService` query paths. When adding searchable fields, update both the migration (the generated tsvector column) and the search query — never search a regular column with `ILIKE` for performance reasons.

## Known limitations

- **No test projects exist.** When asked to add tests, propose a new `Vanadium.Note.REST.Tests` project before writing test code.
- **DTOs are duplicated** between REST and Web projects on purpose (kept simple). Do not introduce a shared `Vanadium.Note.Shared` project unless explicitly requested.
- **No CI workflow** is set up. Build/lint must be verified locally with `dotnet build Vanadium.slnx`.
- **No refresh tokens.** When the JWT expires, the user re-logs in. Don't propose refresh-token implementations without discussion.
- **`appsettings.Development.json` is committed intentionally** with a real dev DB connection and JWT secret. Do not move these values to user-secrets without checking first.

## When making changes

- **Schema changes:** always create an EF Core migration (`dotnet ef migrations add <Name> --project Vanadium.Note.REST`). Never edit existing migration files.
- **New API endpoint:** add (1) DTO in `Vanadium.Note.REST/Models`, (2) DTO mirror in `Vanadium.Note.Web/Models`, (3) HTTP client method in `NoteService`/`LabelService`/etc.
- **Tiptap editor changes:** all JS interop must go through `tiptapInterop.*` in `wwwroot/js/tiptap-editor.js` and be invoked only from `NoteEditor.razor`. Do not call interop from other components.
- **File uploads:** orphan cleanup expects file references to appear as `/uploads/file_{guid}` substrings in note HTML. Changing the URL format requires updating `OrphanFileCleanupJob`.
- **Label categories:** mutual exclusion within a category is enforced server-side in `NoteService`, not via DB constraint. Frontend should not assume DB will reject duplicates.
