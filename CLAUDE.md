# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Goal

Vanadium is a personal project aimed at building a Notion-like note-taking app tailored for developers. The ultimate goal is a custom note app with a developer-friendly workflow.

## Code Conventions

All code, comments, and UI text must be written in English. Korean must not appear anywhere in the codebase.

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
