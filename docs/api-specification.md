# Vanadium Note REST API Specification

This document is the reference contract for the `Vanadium.Note.REST` backend. It lists every
public endpoint exposed by the controllers, along with the HTTP method, path, authentication
requirement, request body/query parameters, response shape, and the notable status codes.

> **Keep this document current.** Whenever a backend API changes (a new endpoint, a changed
> route, request/response shape, auth requirement, or status code), update this file in the same
> change. See `CLAUDE.md` → "When making changes" for the binding rule.

- **Base URL (dev):** `https://localhost:7711`
- **Content type:** `application/json` unless stated otherwise (uploads use `multipart/form-data`).
- **Error shape:** errors are returned as RFC 7807 `ProblemDetails` (`application/problem+json`).
- **No secrets:** this document intentionally contains no connection strings, JWT secrets, or
  password hashes. Those live only in configuration/environment.

## Authentication model

The app is single-user and **password-only** — there is no user identity, no `UserId`, and no
per-row ownership. A successful login mints a JWT carrying a single fixed
`ClaimTypes.Name = "owner"` claim.

Two credential types are accepted on protected endpoints, both via the `Authorization` header
using the `Bearer` scheme. A "smart" authentication scheme routes the request by prefix:

| Credential | Header | Routed to |
|---|---|---|
| JWT (from login) | `Authorization: Bearer <jwt>` | JWT bearer handler |
| Personal access token (PAT) | `Authorization: Bearer van_pat_<...>` | `ApiTokenAuthHandler` |

JWT validation checks only the signing key and expiry — issuer and audience are **not** validated
by design (single-tenant). The default JWT lifetime is 480 minutes (8h), configurable via
`Auth:JwtExpirationMinutes`.

**Auth column legend used below:**

- **Anonymous** — no credential required (`[AllowAnonymous]`).
- **Bearer (JWT or PAT)** — any valid JWT or PAT is accepted (`[Authorize]`).
- **JWT only** — PATs are rejected; only an interactive JWT works (token management endpoints,
  so a token cannot mint or enumerate other tokens).

## Rate limiting

- `POST /api/auth/login` — fixed-window **10 requests/min per client IP** (policy `login`), plus a
  global cross-IP login-backoff lockout that returns `429` with a `Retry-After` header.
- `GET /api/share/{token}` — fixed-window **60 requests/min per client IP** (policy `share`).
- Rate-limit rejections return `429 Too Many Requests`.

---

## Auth — `AuthController`

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous (rate-limited) | Verify the owner password and mint a JWT. |
| POST | `/api/auth/hash` | Anonymous, **Development only** | Compute the storage hash for a password. |

### POST `/api/auth/login`

- **Request body:** `{ "password": string }` (max 256 chars).
- **Response `200`:** `{ "token": string }` — the JWT.
- **Status codes:** `200` success · `401` invalid password · `429` too many attempts / global
  lockout (`Retry-After` header) · `500` server password not configured.

### POST `/api/auth/hash`

Development-only helper that returns the PBKDF2 storage hash for a password so it can be pasted
into `Auth:PasswordHash`. Persists nothing. Returns `404` outside the Development environment.

- **Request body:** `{ "password": string }`.
- **Response `200`:** `{ "hash": string }`.
- **Status codes:** `200` success · `400` password fails the security policy (`ValidationProblemDetails`)
  · `404` not in Development.

---

## Personal access tokens — `ApiTokensController`

Route prefix `/api/apitokens`. **Auth: JWT only** for every endpoint (a PAT cannot manage tokens).

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/apitokens` | JWT only | List all tokens (non-secret view). |
| POST | `/api/apitokens` | JWT only | Create a token; returns the plaintext once. |
| DELETE | `/api/apitokens/{id:guid}` | JWT only | Revoke a token. |

### GET `/api/apitokens`

- **Response `200`:** `ApiTokenSummary[]` — `{ id, name, tokenSuffix, createdAt, expiresAt?, lastUsedAt? }`.
  The full token is never returned here.

### POST `/api/apitokens`

- **Request body:** `CreateApiTokenRequest` — `{ "name": string (≤100), "expiresInDays": int? (1–3650, null = never) }`.
- **Response `200`:** `CreateApiTokenResponse` — `{ id, name, token, createdAt, expiresAt? }`.
  **`token` (plaintext) is shown only in this response and is unrecoverable afterward.**

### DELETE `/api/apitokens/{id:guid}`

- **Status codes:** `204` deleted · `404` not found.

---

## Notes — `NotesController`

Route prefix `/api/notes`. **Auth: Bearer (JWT or PAT)** for every endpoint.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/notes` | Bearer | Paged, filterable, searchable note list. |
| GET | `/api/notes/mention-search` | Bearer | Note suggestions for `@`-mentions. |
| GET | `/api/notes/quick-search` | Bearer | Quick Navigation palette search (includes archived). |
| GET | `/api/notes/summaries` | Bearer | All note summaries (optionally label-filtered). |
| GET | `/api/notes/{id:guid}/children` | Bearer | Direct child notes. |
| GET | `/api/notes/{id:guid}/backlinks` | Bearer | Notes that reference this note. |
| GET | `/api/notes/{id:guid}` | Bearer | Single note (full `NoteItem`). |
| POST | `/api/notes` | Bearer | Create a note. |
| PUT | `/api/notes/{id:guid}` | Bearer | Update a note (optimistic concurrency). |
| DELETE | `/api/notes/{id:guid}` | Bearer | Soft-delete (move to recycle bin). |
| POST | `/api/notes/{id:guid}/archive` | Bearer | Archive a note (read-only). |
| POST | `/api/notes/{id:guid}/unarchive` | Bearer | Unarchive a note. |
| GET | `/api/notes/archive` | Bearer | Paged archive roots. |
| GET | `/api/notes/recycle-bin` | Bearer | Paged recycle-bin roots. |
| POST | `/api/notes/{id:guid}/restore` | Bearer | Restore from recycle bin. |
| DELETE | `/api/notes/{id:guid}/permanent` | Bearer | Permanently delete a recycle-bin note. |
| DELETE | `/api/notes/recycle-bin` | Bearer | Empty the recycle bin. |
| GET | `/api/notes/{id:guid}/share` | Bearer | Get a note's share status. |
| PUT | `/api/notes/{id:guid}/share` | Bearer | Set a note's share mode. |
| DELETE | `/api/notes/{id:guid}/share` | Bearer | Unshare a note. |

### GET `/api/notes`

- **Query:** `page` (default 1, min 1), `pageSize` (default 30, clamped 1–200),
  `search` (≤200 chars), `sortBy` (default `date`), `sortDir` (default `desc`),
  `labelIds` (Guid[], max 50), `includeLabels` (bool, default false).
- **Response `200`:** `PagedResult<NoteSummary>` — `{ items: NoteSummary[], totalCount, page, pageSize, labels? }`.
  `labels` is populated only when `includeLabels=true`. Search results include archived notes
  (`NoteSummary.isArchived = true`); the non-search list excludes them.
- **Status codes:** `200` · `400` more than 50 label IDs.
- **`NoteSummary`:** `{ id, title, updatedAt, parentNoteId?, parentTitle?, childCount, isArchived, labels: LabelSummary[] }`.

### GET `/api/notes/mention-search`

- **Query:** `q` (≤100 chars, default empty).
- **Response `200`:** `MentionSuggestionDto[]` — `{ id, title }`. Excludes archived notes.

### GET `/api/notes/quick-search`

- **Query:** `q` (≤200 chars, default empty), `limit` (default 20, clamped 1–50).
- **Response `200`:** `QuickNavResult[]` — `{ id, title, snippet, isArchived }`. **Includes** archived notes.

### GET `/api/notes/summaries`

- **Query:** `labelIds` (Guid[], max 50).
- **Response `200`:** `NoteSummary[]`. Excludes archived notes.
- **Status codes:** `200` · `400` more than 50 label IDs.

### GET `/api/notes/{id:guid}/children`

- **Response `200`:** `NoteSummary[]` — direct children (archived children excluded).

### GET `/api/notes/{id:guid}/backlinks`

- **Query:** `limit` (default 50).
- **Response `200`:** `BacklinkResult[]` — `{ id, title, snippet, isArchived }`.

### GET `/api/notes/{id:guid}`

- **Response `200`:** full `NoteItem` (see the model below). Works for active and archived notes.
- **Status codes:** `200` · `404` not found (or soft-deleted).

### POST `/api/notes`

- **Request body:** `NoteItem`. Server-owned fields (`shareToken`, `shareMode`, `sharedAt`) are
  forced to the not-shared state and cannot be set here.
- **Response `201`:** the created `NoteItem` (`Location` header points at `GET /api/notes/{id}`).
- **Status codes:** `201` · `400` parent note does not exist, is archived, or is in the recycle bin.

### PUT `/api/notes/{id:guid}`

- **Query:** `force` (bool). `force=true` bypasses the optimistic-concurrency check.
- **Request body:** `NoteItem`. `updatedAt` acts as the concurrency token; share fields are never
  copied from the payload.
- **Response `200`:** the updated `NoteItem`.
- **Status codes:** `200` · `400` invalid parent (self-parent / circular / archived / deleted parent)
  · `403` note is archived and read-only · `409` concurrency conflict · `404` not found.

### DELETE `/api/notes/{id:guid}`

Soft delete: moves the note (and active descendants) to the recycle bin. Accepts archived notes
(keeps `archivedAt`, so a later restore returns them to the Archive).

- **Status codes:** `204` · `404` not found.

### POST `/api/notes/{id:guid}/archive` · POST `/api/notes/{id:guid}/unarchive`

- **Status codes:** `204` · `404` not found (unarchive also `404`s if the note is not archived).

### GET `/api/notes/archive`

- **Query:** `page` (default 1), `pageSize` (default 30, clamped 1–200).
- **Response `200`:** `PagedResult<ArchivedNoteSummary>` — item shape `{ id, title, archivedAt, childCount }`.

### GET `/api/notes/recycle-bin`

- **Query:** `page` (default 1), `pageSize` (default 30, clamped 1–200).
- **Response `200`:** `PagedResult<RecycleBinNoteSummary>` — item shape `{ id, title, deletedAt, childCount, isArchived }`.

### POST `/api/notes/{id:guid}/restore`

- **Status codes:** `204` · `404` not found in recycle bin.

### DELETE `/api/notes/{id:guid}/permanent`

- **Status codes:** `204` · `404` not found · `409` note is not in the recycle bin (move it there first).

### DELETE `/api/notes/recycle-bin`

Empties the recycle bin (permanent delete of all recycle-bin notes).

- **Status codes:** `204`.

### GET `/api/notes/{id:guid}/share`

- **Response `200`:** `ShareInfo` — `{ isShared, mode, token?, sharedAt? }`. `token` is exposed to the
  owner so the client can build the shareable link.
- **Status codes:** `200` · `404` not found.

### PUT `/api/notes/{id:guid}/share`

- **Request body:** `SetShareRequest` — `{ "mode": "None" | "Public" | "Link" }`. `None` is equivalent
  to unsharing.
- **Response `200`:** `ShareInfo`.
- **Status codes:** `200` · `404` not found.

### DELETE `/api/notes/{id:guid}/share`

Unshares the note, immediately and permanently invalidating any previously issued link.

- **Status codes:** `204` · `404` not found.

---

## Labels & categories — `LabelsController`

**Auth: Bearer (JWT or PAT)** for every endpoint. Routes are explicit (not the controller name).

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/label-categories` | Bearer | List categories (with their labels). |
| POST | `/api/label-categories` | Bearer | Create a category. |
| DELETE | `/api/label-categories/{id:guid}` | Bearer | Delete a category. |
| GET | `/api/labels` | Bearer | List all labels. |
| POST | `/api/labels` | Bearer | Create a label. |
| DELETE | `/api/labels/{id:guid}` | Bearer | Delete a label. |
| POST | `/api/notes/{noteId:guid}/labels` | Bearer | Assign a label to a note. |
| DELETE | `/api/notes/{noteId:guid}/labels/{labelId:guid}` | Bearer | Remove a label from a note. |

### GET `/api/label-categories`

- **Response `200`:** `LabelCategoryDto[]` — `{ id, name, labels: LabelSummary[] }`.

### POST `/api/label-categories`

- **Request body:** `NameRequest` — `{ "name": string (required, ≤100) }`.
- **Response `201`:** `LabelCategoryDto`.
- **Status codes:** `201` · `409` duplicate name.

### DELETE `/api/label-categories/{id:guid}`

- **Status codes:** `204` · `404` not found.

### GET `/api/labels`

- **Response `200`:** `LabelSummary[]` — `{ id, name, categoryId?, categoryName? }`.

### POST `/api/labels`

- **Request body:** `CreateLabelRequest` — `{ "name": string (required, ≤100), "categoryId": Guid? }`.
- **Response `201`:** `LabelSummary`.
- **Status codes:** `201` · `409` duplicate name.

### DELETE `/api/labels/{id:guid}`

- **Status codes:** `204` · `404` not found.

### POST `/api/notes/{noteId:guid}/labels`

Assigns a label to a note. Within a category, labels are mutually exclusive (adding one removes
others in the same category).

- **Request body:** `AddLabelRequest` — `{ "labelId": Guid }`.
- **Status codes:** `200` · `404` note or label not found · `403` note is archived and read-only.

### DELETE `/api/notes/{noteId:guid}/labels/{labelId:guid}`

- **Status codes:** `204` · `404` note not found or label not assigned · `403` note is archived and read-only.

---

## File attachments — `FilesController`

Route prefix `/api/files`. Files are stored on disk as `file_{guid}` with a metadata row.

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/files` | Bearer | Upload a file attachment (`multipart/form-data`). |
| GET | `/api/files/{id:guid}` | Bearer | Download a file attachment. |

### POST `/api/files`

- **Request:** `multipart/form-data` with a `file` field. Max **100 MB**.
- **Validation:** 13-type MIME whitelist (`application/pdf`, `application/zip`, Word/Excel legacy +
  OOXML, `text/plain`, `text/markdown`, `image/jpeg`, `image/png`, `image/gif`, `image/webp`), plus
  magic-byte / text-sniff content validation.
- **Response `200`:** `{ "url": "/api/files/{guid}", "filename": string }`. The `url` is the reference
  form embedded in note HTML.
- **Status codes:** `200` · `400` no file / disallowed type / content mismatch.

### GET `/api/files/{id:guid}`

- **Response `200`:** the file stream (original content type and filename).
- **Status codes:** `200` · `404` metadata or physical file missing.

---

## Images — `ImagesController`

Route prefix `/api/images`. Images are stored on disk as `{guid}{ext}` with **no** DB record.

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/images` | Bearer | Upload an image (`multipart/form-data`). |
| GET | `/api/images/{id:guid}` | Bearer | Fetch an image (cached 1 year, client). |

### POST `/api/images`

- **Request:** `multipart/form-data` with a `file` field. Max **10 MB**.
- **Validation:** magic-byte detection for JPEG, PNG, GIF, WebP (declared content type is not trusted).
- **Response `200`:** `{ "url": "/api/images/{guid}" }`.
- **Status codes:** `200` · `400` no file / unsupported format.

### GET `/api/images/{id:guid}`

- **Response `200`:** the image stream, `Cache-Control` 1 year (client).
- **Status codes:** `200` · `404` not found.

---

## Settings — `SettingsController`

Route prefix `/api/settings`. **Auth: Bearer (JWT or PAT).** `UserSettings` is a singleton row.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/settings` | Bearer | Get user settings. |
| PUT | `/api/settings` | Bearer | Update user settings. |
| DELETE | `/api/settings/all-data` | **JWT only** | Permanently purge all content (owner-password re-confirmation required). |

### GET `/api/settings`

- **Response `200`:** `UserSettings` — `{ id, defaultSortBy, defaultSortDir, defaultPageSize, theme }`.

### PUT `/api/settings`

- **Request body:** `UserSettings`.
- **Response `200`:** the saved `UserSettings`.

### DELETE `/api/settings/all-data`

Permanently deletes all notes, labels, label categories, API tokens, settings, and orphaned
uploads. The owner password lives in configuration, so login remains possible afterward.

As the most destructive endpoint, it is locked down beyond the shared smart scheme: it accepts
**only the interactive JWT scheme** (a personal access token cannot call it), and it re-confirms
the owner password from the request body before deleting anything.

- **Request body:** `{ password }` — the owner password, re-verified with `PasswordHasher.Verify`.
- **Status codes:** `204` purged · `403` wrong password · `401` called with a PAT (or no JWT) · `500` server password not configured.

---

## Shared notes (anonymous) — `ShareController`

Route prefix `/api/share`. **Auth: Anonymous** (`[AllowAnonymous]`), rate-limited (`share`, 60/min per IP).
This is the only content endpoint reachable without authentication.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/share/{token}` | Anonymous (rate-limited) | Read a shared note by its share token. |

### GET `/api/share/{token}`

- **Path:** `token` — the unguessable share token.
- **Response `200`:** `SharedNote` — `{ id, title, content, updatedAt }`. Lean, read-only; never exposes
  the token, structure, labels, or lifecycle fields. For `Link` mode, an `X-Robots-Tag: noindex, nofollow`
  header is added; `Public` mode omits it.
- **Status codes:** `200` · `404` unknown, revoked, or soft-deleted note.

---

## Core model shapes

### `NoteItem` (full note)

Returned by `GET /api/notes/{id}`, `POST /api/notes`, `PUT /api/notes/{id}`.

| Field | Type | Notes |
|---|---|---|
| `id` | Guid | |
| `title` | string | ≤500 chars |
| `content` | string | HTML from the Tiptap editor, ≤2,000,000 chars |
| `updatedAt` | DateTime (UTC) | Optimistic-concurrency token |
| `deletedAt` | DateTime? | Non-null = soft-deleted |
| `archivedAt` | DateTime? | Non-null = archived (read-only) |
| `shareToken` | string? | Server-owned; null when not shared |
| `shareMode` | enum string | `None` / `Public` / `Link` |
| `sharedAt` | DateTime? | Server-owned |
| `parentNoteId` | Guid? | |
| `childCount` | int | Computed |
| `parentTitle` | string? | Computed |
| `labels` | LabelSummary[] | Computed |

> `contentText`, `isDeletionRoot`, `isArchiveRoot`, and navigation properties are `[JsonIgnore]` and
> not part of the wire contract.

### `PagedResult<T>`

`{ items: T[], totalCount: int, page: int, pageSize: int, labels: LabelSummary[]? }`

### `LabelSummary`

`{ id: Guid, name: string, categoryId: Guid?, categoryName: string? }`

### `UserSettings`

`{ id: Guid, defaultSortBy: string (≤20), defaultSortDir: string (≤4), defaultPageSize: int, theme: string (≤6) }`
