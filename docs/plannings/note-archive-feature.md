# Feature Specification: Note Archive

Status: Draft for review
Author: smoh (written with Claude)
Date: 2026-06-11
Companion doc: `docs/recycle-bin-feature.md`

## 1. Overview

As notes accumulate, the main list (`/`, Home) and the board (`/board`) become cluttered with notes that are finished or no longer actively used. The Recycle Bin is not a fit for long-term storage: items there are permanently purged after 30 days. This feature introduces an **Archive** — a third, intermediate note state between "active" and "deleted":

- Archived notes are **kept forever** (no automatic purge).
- They are **hidden** from Home and Board, but **included in full-text search** with an "Archived" badge.
- They are **read-only**: viewable in the editor, but title/content/labels cannot be modified. Enforced server-side.
- Archiving a note archives its active sub-notes; unarchiving restores them together (root-tracking pattern mirroring the Recycle Bin's `IsDeletionRoot`).
- A dedicated `/archive` page lists archived notes and allows unarchive / delete.

### Goals

- Provide a lossless "put away" state that declutters daily working views.
- Keep archived content findable via search and openable for reading.
- Keep attachments of archived notes alive (orphan file cleanup must treat archived content as live references).
- Define unambiguous state transitions between Active, Archived, and Recycle Bin.

### Non-goals

- Auto-archiving by age or inactivity rules.
- Archiving labels, label categories, or standalone file attachments.
- Per-note retention or expiry (archive is indefinite by definition).
- Version history / snapshots.
- Board-view drop target for archiving (v2 candidate).

## 2. Current Behavior (Baseline)

| Concern | Today |
|---|---|
| Note states | Active, or soft-deleted (`DeletedAt != null`, Recycle Bin, purged after `RecycleBin:RetentionDays`) |
| Hiding soft-deleted notes | EF Core global query filter `n => n.DeletedAt == null` on `NoteItem` (and a matching filter on `NoteLabel`); Recycle Bin paths opt out via `IgnoreQueryFilters()` |
| Long-term storage | None — users either keep notes in the main list or risk losing them via the Recycle Bin |
| Search | `NoteService.ApplyFilters()` applies `EF.Functions.ILike` per whitespace-separated term against `Title` + `ContentText`, backed by a GIN trigram index (`gin_trgm_ops` on `(Title, ContentText)`). Note: `CLAUDE.md` still describes a tsvector column; the code switched to trigram search in migration `20260426140544_SwitchToTrigramSearch`. This feature is agnostic to the index type — it only adds a `WHERE` predicate change and a DTO flag. |
| Read-only enforcement | None — any owned, non-deleted note is writable via `PUT /api/notes/{id}` |
| Editor | `NoteEditor.razor` + Tiptap via `tiptapInterop.*`; no read-only mode exposed by the interop yet |

## 3. Requirements

### 3.1 Functional requirements

- **FR-1 Archive with sub-notes**: Archiving a note sets it and all of its *active* descendants to archived in one operation. Descendants already in the Recycle Bin are not touched. The directly archived note is tracked as the archive root.
- **FR-2 Unarchive with sub-notes**: Unarchiving restores the note and the descendants that were archived in the same operation (same-group tracking, see §6.1). Independently archived descendants (their own archive roots) stay archived.
- **FR-3 Read-only**: Archived notes reject all mutations server-side: title/content/parent updates, label add/remove, and creating sub-notes under them. Reading (`GET /api/notes/{id}`) remains allowed.
- **FR-4 Visibility**:
  - Excluded from: Home list (non-search), Board summaries, children listing of active notes, mention search.
  - Included in: full-text search results, flagged `IsArchived = true` so the UI can render an "Archived" badge.
  - Listed in: a new `/archive` page (archive roots only, newest first).
- **FR-5 Deletion of archived notes**: An archived note can be moved to the Recycle Bin. Archive state is preserved through the Recycle Bin round-trip: restoring it from the Recycle Bin returns it to the Archive, not to the active list (lossless restore, consistent with the Recycle Bin's design philosophy).
- **FR-6 No automatic purge**: Archived notes are retained indefinitely. No background job deletes them.
- **FR-7 Attachments stay alive**: Files referenced by archived notes' HTML (`/uploads/file_{guid}`) must never be considered orphans.
- **FR-8 Hierarchy integrity**: No active note may have an archived parent. Re-parenting onto an archived note and creating a child of an archived note are rejected. Restore paths that would violate this re-attach the note as a root note (`ParentNoteId = null`).

### 3.2 Non-functional requirements

- **NFR-1** No new external dependencies (MudBlazor, EF Core, and existing Tiptap setup suffice).
- **NFR-2** Schema change via a new EF Core migration; existing migrations untouched.
- **NFR-3** No `ILIKE` against non-indexed columns; the archive predicate is a plain `WHERE` clause and does not change search indexing.
- **NFR-4** DTOs duplicated between REST and Web projects per convention; no shared project.
- **NFR-5** All code, comments, and UI text in English.
- **NFR-6** Structured Serilog logging for archive/unarchive operations (correlation ID auto-enriched).
- **NFR-7** Archive list endpoint paginates (same `PagedResult<T>` pattern as the Recycle Bin).
- **NFR-8** Account wipe (`AccountService.PurgeAllDataAsync`) must delete archived notes too.

## 4. State Model

Two independent nullable timestamps define three user-visible states. `DeletedAt` takes display precedence over `ArchivedAt`.

```text
                 archive                       delete (soft)
   ┌──────────┐ ────────────► ┌──────────┐ ────────────────► ┌───────────────────┐
   │  Active  │               │ Archived │                   │ Recycle Bin       │
   │          │ ◄──────────── │ (read-   │ ◄──────────────── │ (ArchivedAt kept) │
   └──────────┘   unarchive   │  only)   │  restore          └───────────────────┘
        │                     └──────────┘   (returns to              │
        │ delete (soft)                       Archive)                │ purge job /
        ▼                                                             ▼ permanent delete
   ┌───────────────────┐    restore (returns to Active)         ┌──────────┐
   │ Recycle Bin       │ ◄───────────────────────────────────── │  Gone    │
   │ (ArchivedAt null) │                                        └──────────┘
   └───────────────────┘
```

State transition table:

| From | Action | To | Notes |
|---|---|---|---|
| Active | `POST /{id}/archive` | Archived | Sweeps active descendants (FR-1) |
| Archived | `POST /{id}/unarchive` | Active | Restores same-group descendants (FR-2); root re-attach fallback (FR-8) |
| Archived | `DELETE /{id}` | Recycle Bin | `ArchivedAt` is **kept**; sweeps descendants per existing soft-delete logic |
| Recycle Bin (`ArchivedAt != null`) | `POST /{id}/restore` | Archived | Existing restore clears only `DeletedAt`/`IsDeletionRoot` — no code change needed for this row |
| Recycle Bin | purge / permanent delete | Gone | Unchanged; archive adds no retention logic |
| Active | `POST /{id}/unarchive` | — | 404 (not an archived note) |
| Archived | `POST /{id}/archive` | Archived | 204 no-op (idempotent) |
| Recycle Bin | `POST /{id}/archive` | — | 404 (deleted notes are invisible to the archive endpoint via the global filter) |

Invariants:

- **INV-1**: If a note is archived, every *active* ancestor path above it contains no archived note → equivalently, no active note has an archived ancestor. (Enforced at archive, unarchive, restore, create, and re-parent time.)
- **INV-2**: All notes archived in one operation share the same `ArchivedAt` timestamp (group identity, mirroring the Recycle Bin's shared `DeletedAt`).
- **INV-3**: `IsArchiveRoot = true` only on the note the user archived directly.

## 5. Data Model

### 5.1 Schema changes

Add to `NoteItem` (`Vanadium.Note.REST/Models/NoteItem.cs`):

```csharp
/// <summary>Null = not archived. Non-null marks the note archived (read-only).
/// Notes archived in the same operation share the same value (restore group).</summary>
public DateTime? ArchivedAt { get; set; }

/// <summary>True only on the note the user archived directly.
/// Sub-notes swept into the archive keep this false.</summary>
[JsonIgnore]
public bool IsArchiveRoot { get; set; }
```

- `ArchivedAt` is **serialized** (no `[JsonIgnore]`), unlike `IsDeletionRoot` — the frontend editor needs it to switch to read-only mode (§9.3). Mirror the property on the Web project's `NoteItem` model.
- Set `ArchivedAt` with the same `UtcNowMicroseconds()` truncation used for `DeletedAt`.

### 5.2 No global query filter for archive — explicit predicates instead

**Decision: do NOT extend the global query filter.** This is the single most important design choice, for three reasons:

1. **EF Core allows one `HasQueryFilter` per entity.** Combining into `n => n.DeletedAt == null && n.ArchivedAt == null` would make every `IgnoreQueryFilters()` call drop *both* conditions. Every existing Recycle Bin opt-out path (recycle bin list/restore/permanent delete, `RecycleBinPurgeJob`, both `FileCleanupService` scans, `AccountService` wipe) would suddenly see archived notes too — mostly harmless by luck, but each would need re-auditing, and future opt-outs would carry a permanent hidden coupling.
2. **Archive visibility is not uniform.** Soft-deleted notes are hidden *everywhere*, which is what a global filter is for. Archived notes are hidden in some reads (Home, Board, children, mentions) but visible in others (search, `GET /{id}`, archive page). A global filter would force `IgnoreQueryFilters()` onto the *search and single-note paths* — which would then also expose soft-deleted notes there, a correctness bug requiring manual re-filtering of `DeletedAt`.
3. **File cleanup safety for free.** Because archived notes remain visible to default queries, both `FileCleanupService` content scans (which already use `IgnoreQueryFilters()` for the Recycle Bin) see archived content with **zero changes** — FR-7 is satisfied structurally. Likewise `AccountService.PurgeAllDataAsync` (already `IgnoreQueryFilters()`) deletes archived notes — NFR-8 satisfied with zero changes.

Instead, add explicit predicates (`Where(n => n.ArchivedAt == null)`) to exactly the read paths that must exclude archived notes (enumerated in §8.1). The compile-time cost is a handful of one-line `Where` clauses; the benefit is that nothing is hidden implicitly.

> Reviewed per the repo rule "any new code that scans note content or bulk-deletes notes must consider `IgnoreQueryFilters()`": this feature adds no new content scans or bulk deletes, and because no new global filter is introduced, no existing opt-out path needs modification. The existing `DeletedAt == null` filter continues to apply to all new archive queries (e.g., the archive page never shows notes that are both archived and in the Recycle Bin).

### 5.3 Index and migration

- Partial index for archive list queries and the Home/Board exclusion predicate:

```csharp
modelBuilder.Entity<NoteItem>()
    .HasIndex(n => n.ArchivedAt)
    .HasFilter("\"ArchivedAt\" IS NOT NULL");
```

- Migration: `dotnet ef migrations add AddNoteArchive --project Vanadium.Note.REST`.
- Existing rows: `ArchivedAt = NULL`, `IsArchiveRoot = false` (column defaults) — no backfill.
- The GIN trigram index on `(Title, ContentText)` is unchanged.

## 6. Processing Flows

### 6.1 Archive (with sub-note sweep)

```text
ArchiveAsync(userId, id):
  note = db.Notes.FirstOrDefault(n => n.Id == id && n.UserId == userId)
         // global filter: returns null for recycle-bin notes → 404
  if note == null            → return NotFound
  if note.ArchivedAt != null → return Success (idempotent no-op)

  now = UtcNowMicroseconds()
  group = [note] + CollectActiveDescendantsAsync(id)
          // existing BFS helper; global filter excludes recycle-bin descendants;
          // additionally skip n.ArchivedAt != null descendants (already-archived
          // subtrees keep their own root/group — see edge case E3)

  foreach n in group where n.ArchivedAt == null:
      n.ArchivedAt = now
  note.IsArchiveRoot = true
  SaveChanges
  log "Note {NoteId} archived with {DescendantCount} descendant(s) by {UserId}"
```

### 6.2 Unarchive (group restore + root re-attach fallback)

```text
UnarchiveAsync(userId, id):
  note = db.Notes.FirstOrDefault(n => n.Id == id && n.UserId == userId
                                   && n.ArchivedAt != null)
  if note == null → return NotFound

  group = [note] + CollectArchivedGroupDescendantsAsync(id, note.ArchivedAt)
          // new BFS helper mirroring CollectDeletedGroupDescendantsAsync:
          // descend only through notes with ArchivedAt == note.ArchivedAt
          // (independently archived subtrees stay archived)

  foreach n in group:
      n.ArchivedAt = null
      n.IsArchiveRoot = false

  // FR-8 / INV-1: never resurrect under an invisible or archived parent
  if note.ParentNoteId != null:
      parent = db.Notes.IgnoreQueryFilters()
                 .FirstOrDefault(p => p.Id == note.ParentNoteId)
      if parent == null || parent.DeletedAt != null || parent.ArchivedAt != null:
          note.ParentNoteId = null   // re-attach as root note

  SaveChanges
  log "Note {NoteId} unarchived with {DescendantCount} descendant(s) by {UserId}"
```

### 6.3 Search including archived notes

`NoteService.GetPaged()` keeps a single query; the archive predicate applies only when *not* searching:

```text
GetPaged(userId, page, pageSize, search, sortBy, sortDir, labelIds):
  q = db.Notes.Where(n => n.UserId == userId)        // global filter hides deleted
  if string.IsNullOrWhiteSpace(search):
      q = q.Where(n => n.ArchivedAt == null)          // Home list: no archived notes
      q = q.Where(n => n.ParentNoteId == null)        // existing root-only rule
  else:
      // archived notes INCLUDED; ApplyFilters unchanged (ILike per term,
      // GIN trigram-backed — no new ILIKE on non-indexed columns)
      q = ApplyFilters(q, search, labelIds)
  ...projection adds IsArchived = n.ArchivedAt != null into NoteSummary
```

### 6.4 Soft delete of an archived note (no code change expected)

The existing `Delete()` collects active descendants through the default-filtered set. Since archive adds no global filter, archived descendants are naturally included in the sweep, and `ArchivedAt` is simply left untouched. Existing `Restore()` clears only `DeletedAt`/`IsDeletionRoot`, so the note returns to the Archive (FR-5). The only `Restore()` change: the re-attach fallback must also trigger when the parent is archived **and the restored root itself is not archived** (otherwise an active note would resurrect under an archived parent — edge case E2).

### 6.5 Write-path guard

Central guard used by every mutation path (§8.1):

```text
if note.ArchivedAt != null → 403 Forbidden, ProblemDetails "Note is archived and read-only."
```

403 (not 409) because the editor already interprets 409 as an optimistic-concurrency conflict and offers Force Save / Reload — wrong UX for an archived note.

## 7. API Design

All endpoints live in `NotesController`, scoped by the existing `GetUserId()` pattern.

### 7.1 New endpoints

| Method & route | Behavior | Responses |
|---|---|---|
| `POST /api/notes/{id}/archive` | Archive note + active descendants (§6.1). No-op if already archived. | 204 / 404 |
| `POST /api/notes/{id}/unarchive` | Unarchive note + same-group descendants (§6.2). | 204 / 404 |
| `GET /api/notes/archive?page=&pageSize=` | Paged list of archive roots (`IsArchiveRoot && ArchivedAt != null`), `ArchivedAt` desc. Global filter automatically excludes archived notes that are currently in the Recycle Bin. | 200 `PagedResult<ArchivedNoteSummary>` |

### 7.2 Modified endpoints

| Method & route | Change |
|---|---|
| `GET /api/notes` | Excludes archived notes when `search` is empty; includes them (with `IsArchived` flag) when searching (§6.3) |
| `GET /api/notes/summaries` | Add `Where(n => n.ArchivedAt == null)` — Board never shows archived notes |
| `GET /api/notes/{id}/children` | Add `Where(n => n.ArchivedAt == null)` — Home expansion of active notes |
| `GET /api/notes/mention-search` | Add `Where(n => n.ArchivedAt == null)` — mentions are for active work (see open question O1) |
| `GET /api/notes/{id}` | Unchanged behavior (archived notes remain retrievable); response now carries `ArchivedAt` |
| `PUT /api/notes/{id}` | 403 if target note is archived (before concurrency check). Also: reject `ParentNoteId` pointing at an archived note (400, mirroring the existing recycle-bin parent check) |
| `POST /api/notes` | Reject `ParentNoteId` pointing at an archived note (400) |
| `POST /api/notes/{noteId}/labels`, `DELETE /api/notes/{noteId}/labels/{labelId}` | 403 if the note is archived |
| `DELETE /api/notes/{id}` | Unchanged route; now also accepts archived notes (moves them to the Recycle Bin, §6.4) |
| `DELETE /api/notes/{id}/permanent` | Unchanged — still 409 for *active* notes; archived-but-not-deleted notes are "active" for this check (must go through the Recycle Bin first; no bypass) |

### 7.3 DTOs

New DTO, defined in `Vanadium.Note.REST/Models/ArchivedNoteSummary.cs` and mirrored in `Vanadium.Note.Web/Models` per convention:

```csharp
public class ArchivedNoteSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime ArchivedAt { get; set; }
    public int ChildCount { get; set; }   // descendants in the same archive group
}
```

Modified DTO — `NoteSummary` (both projects):

```csharp
public bool IsArchived { get; set; }   // true only in search results; drives the badge
```

Modified model — Web `NoteItem` mirror:

```csharp
public DateTime? ArchivedAt { get; set; }   // editor read-only switch
```

## 8. Interface Design

### 8.1 Backend changes by file

| File | Change |
|---|---|
| `Models/NoteItem.cs` | Add `ArchivedAt`, `IsArchiveRoot` (§5.1) |
| `Models/ArchivedNoteSummary.cs` | New DTO |
| `Models/NoteSummary.cs` | Add `IsArchived` |
| `Data/NoteDbContext.cs` | Partial index on `ArchivedAt` (§5.3). **No global filter change.** |
| `Migrations/` | New `AddNoteArchive` migration |
| `Services/NoteService.cs` | New methods (below); `ArchivedAt == null` predicates in `GetPaged` (non-search branch), `GetAllSummaries`, `GetChildren`, `SearchForMention`; archived guard in `Update`; archived-parent validation in `Create`/`Update`; archived-parent fallback in `Restore` |
| `Services/LabelService.cs` (or wherever note-label assignment lives) | Archived guard before add/remove label |
| `Controllers/NotesController.cs` | Three new endpoints (§7.1); 403/400 mappings; structured logs |

New `NoteService` method signatures (REST):

```csharp
public async Task<bool> Archive(Guid userId, Guid id);          // false = not found
public async Task<bool> Unarchive(Guid userId, Guid id);        // false = not found / not archived
public async Task<PagedResult<ArchivedNoteSummary>> GetArchive(Guid userId, int page, int pageSize);

private async Task<List<NoteItem>> CollectArchivedGroupDescendantsAsync(Guid rootId, DateTime groupArchivedAt);
```

No new hosted service: FR-6 means there is deliberately **no** `ArchivePurgeJob` counterpart to `RecycleBinPurgeJob`.

### 8.2 Frontend changes by file

| File | Change |
|---|---|
| `Models/ArchivedNoteSummary.cs` (Web) | New DTO mirror |
| `Models/NoteSummary.cs` (Web) | Add `IsArchived` |
| `Models/NoteItem.cs` (Web) | Add `ArchivedAt` |
| `Services/NoteService.cs` (Web) | New client methods following the `ServiceResult<T>` pattern (below) |
| `Pages/Archive.razor` | New `/archive` page (§9.2) |
| `Pages/Home.razor` | "Archive" row action + confirmation dialog + Undo snackbar; "Archived" badge on search results; clicking an archived result opens the read-only editor |
| `Pages/NoteEditor.razor` | Read-only mode when `ArchivedAt != null` (§9.3); "Archive" action in the editor menu for active notes |
| `Layout/NavMenu.razor` | "Archive" nav item (archive icon) between Board and Recycle Bin |
| `wwwroot/js/tiptap-editor.js` | Add `editable` option to `tiptapInterop.init` (or a `tiptapInterop.setEditable(id, bool)` function) — invoked only from `NoteEditor.razor` per the interop rule |

New Web `NoteService` client methods:

```csharp
public async Task<ServiceResult<bool>> ArchiveAsync(Guid id);      // POST api/notes/{id}/archive
public async Task<ServiceResult<bool>> UnarchiveAsync(Guid id);    // POST api/notes/{id}/unarchive
public async Task<ServiceResult<PagedResult<ArchivedNoteSummary>>> GetArchiveAsync(int page = 1, int pageSize = 50);
```

## 9. User Scenarios / UX

### 9.1 Archiving from Home or the editor

- Home row action and editor menu item: **Archive**.
- Confirmation dialog: "Archive this note? Sub-notes will be archived too. Archived notes are read-only and kept indefinitely."
- On success: snackbar "Note archived" with **Undo** action (calls `UnarchiveAsync`), mirroring the Recycle Bin's undo pattern. Editor navigates back to Home.

### 9.2 Archive page (`/archive`)

- Lists archive roots only (swept sub-notes are not shown individually), `ArchivedAt` desc: title, archived date, sub-note count.
- Row actions: **Unarchive**, **Delete** (moves to Recycle Bin, with the standard recycle-bin confirmation dialog). Clicking the title opens the read-only editor.
- Empty state text: "Nothing archived yet. Archive a note from the note list or the editor to keep it without cluttering your workspace."
- Built from existing MudBlazor components; reuse `ConfirmDialog.razor`.

### 9.3 Read-only editor

When `note.ArchivedAt != null`:

- Banner at the top: "This note is archived and read-only." with an **Unarchive** button (on success, the editor becomes editable in place).
- Title field disabled; Tiptap initialized with `editable: false`; label picker, save button, and "+ Sub-note" hidden; auto-save debounce never armed.
- Server remains the source of truth: even a stale/bypassing client gets 403 on `PUT`.

### 9.4 Search

- Archived notes appear in Home search results with an "Archived" MudChip next to the title (`IsArchived`).
- Selecting one navigates to `/editor/{id}` in read-only mode.

### 9.5 Use cases (summary)

| # | Actor goal | Flow | Outcome |
|---|---|---|---|
| UC-1 | Put away a finished project note tree | Home → Archive action → confirm | Tree vanishes from Home/Board; findable via search and `/archive` |
| UC-2 | Look something up in an archived note | Search → result with badge → open | Read-only editor; attachments render |
| UC-3 | Resume work on an archived tree | `/archive` → Unarchive (or editor banner button) | Tree returns to Home/Board with hierarchy intact |
| UC-4 | Discard an archived note for good | `/archive` → Delete → Recycle Bin → permanent delete or 30-day purge | Standard recycle-bin lifecycle |
| UC-5 | Recover an accidentally deleted archived note | Recycle Bin → Restore | Note returns to the **Archive** (lossless) |

## 10. Edge Cases

- **E1 — Re-parenting onto an archived note**: `PUT /api/notes/{id}` with `ParentNoteId` of an archived note → 400, mirroring the existing recycle-bin parent rejection. Same for `POST /api/notes` (INV-1).
- **E2 — Recycle Bin restore under an archived parent**: A note deleted while active, whose parent was archived in the meantime, would resurrect invisibly under a read-only parent. Extend `Restore()`'s existing fallback: if the parent is missing, soft-deleted, **or archived** (and the restored root itself is not archived), re-attach as a root note. If the restored root *is* archived (UC-5), an archived parent is a legal home — no detach.
- **E3 — Archiving a parent of an already-archived subtree**: The earlier subtree keeps its own `IsArchiveRoot` and earlier `ArchivedAt`; the sweep skips it (§6.1). Both appear in `/archive` independently and unarchive independently. Unarchiving the (outer) parent leaves the inner subtree archived; its later unarchive finds its parent active and keeps `ParentNoteId`. Unarchiving the inner subtree first triggers the §6.2 fallback (parent still archived) → re-attached as root. Same semantics as the Recycle Bin's nested-deletion handling.
- **E4 — Some descendants already in the Recycle Bin at archive time**: They are invisible to the sweep (global filter) and keep `ArchivedAt = null`. If later restored from the Recycle Bin, E2's fallback applies (parent archived → detach to root).
- **E5 — Write API calls against archived notes**: `PUT`, label add/remove → 403 with ProblemDetails. A stale editor session that was open when another session archived the note gets 403 on the next auto-save; the editor surfaces "This note has been archived" and switches to read-only rather than the concurrency-conflict dialog.
- **E6 — Archive a recycle-bin note / unarchive an active note**: Both 404 (the first via the global filter, the second via the `ArchivedAt != null` lookup).
- **E7 — Mentions and page links pointing at archived notes**: Keep rendering; clicking opens the read-only editor (unlike recycle-bin targets, which 404). No content rewriting on archive/unarchive — transitions stay O(subtree) and lossless.
- **E8 — `DELETE /api/notes/{id}/permanent` on an archived (not deleted) note**: Still 409 — the Recycle Bin remains the only path to destruction; archive grants no shortcut.
- **E9 — Attachments**: Archived content is visible to both `FileCleanupService` scans by construction (§5.2) — no early garbage collection. Verified explicitly in T-11.
- **E10 — Account wipe**: `PurgeAllDataAsync` already uses `IgnoreQueryFilters()` and, with no archive global filter, sees archived notes in any case. Covered by T-12.
- **E11 — Concurrency between archive and auto-save**: `Archive()` does not bump `UpdatedAt` (content unchanged). An in-flight auto-save that lands after archiving hits the §6.5 guard → 403 (E5). One save landing a moment *before* the archive is acceptable last-write behavior.

## 11. Test Scenarios

Prerequisite: no test project exists. **Proposal: new `Vanadium.Note.REST.Tests` project** (xUnit + the EF Core in-memory or SQLite provider for `NoteService`-level tests; PostgreSQL-only features like trigram search stay out of unit scope). This is a new project-level dependency and needs sign-off per repo policy before implementation.

| # | Type | Scenario | Expected |
|---|---|---|---|
| T-1 | Normal | Archive a root note with 2 active children | All 3 share `ArchivedAt`; root has `IsArchiveRoot`; gone from `GetPaged` (no search) and `GetAllSummaries` |
| T-2 | Normal | Unarchive that root | All 3 active again; flags cleared; hierarchy intact |
| T-3 | Normal | Search matches an archived note | Included in results with `IsArchived = true`; non-search list excludes it |
| T-4 | Normal | `Get` on archived note | Returned with `ArchivedAt` set |
| T-5 | Boundary | Archive an already-archived note | 204 no-op; `ArchivedAt`/group unchanged |
| T-6 | Boundary | Archive parent over an already-archived subtree (E3), then unarchive in both orders | Groups stay independent; inner-first unarchive re-attaches to root |
| T-7 | Boundary | Archive when one descendant is in the Recycle Bin (E4); then restore that descendant | Sweep skips it; restore detaches it to root |
| T-8 | Boundary | Unarchive when the archive root's parent was deleted/purged meanwhile | Re-attached as root note |
| T-9 | Failure | `PUT`, add-label, remove-label on archived note | 403 each |
| T-10 | Failure | Create / re-parent under an archived parent | 400; archive/unarchive on wrong-state targets (E6) → 404; cross-user access → 404 |
| T-11 | Normal | Archived note with attachment; run `DeleteAllOrphansAsync` | File survives; after permanent delete via the Recycle Bin, file is removed |
| T-12 | Normal | Account wipe with archived + deleted + active notes | All rows gone |
| T-13 | Normal | Delete archived root → restore from Recycle Bin | Returns to Archive (`ArchivedAt` preserved), listed in `/archive` again |
| T-14 | Boundary | `DELETE /{id}/permanent` on archived-but-not-deleted note | 409 |
| T-15 | Manual/UI | Read-only editor: banner, disabled title, non-editable Tiptap, no auto-save; Undo snackbar; "Archived" search badge; `/archive` page actions | Per §9 |

Manual verification per repo workflow: `dotnet build Vanadium.slnx` clean; `dotnet ef database update` on dev DB; Swagger pass over the three new endpoints and the 403/400/404/409 matrix; both apps running for the UI pass.

## 12. Implementation Order

1. Migration + entity properties + index (no behavior change yet; deploy-safe).
2. `NoteService`: `Archive`/`Unarchive`/`GetArchive` + group BFS helper + visibility predicates + write guards + `Restore` fallback extension. Controller endpoints + logging. Verify via Swagger.
3. Web DTOs + client methods + `Archive.razor` + NavMenu.
4. `NoteEditor.razor` read-only mode + `tiptapInterop` `editable` support + Home archive action/badge/undo.
5. Tests (pending sign-off on the test project) + update `CLAUDE.md` (archive state exists; no global filter for archive — explain why; new endpoints; tsvector→trigram correction while at it).

## 13. Open Questions

- **O1 — Mention search**: v1 excludes archived notes from `@`-mention autocomplete (mentions target active work). Should there be a toggle to mention archived notes (e.g., when writing a retrospective)?
- **O2 — Archive entry points**: v1 adds Home row action + editor menu. Also add bulk archive (multi-select on Home, like bulk delete) and/or a Board column action? Bulk archive looks cheap to add (same endpoint per id) — confirm scope.
- **O3 — Visual treatment of links to archived notes**: v1 renders mentions/page links to archived notes normally (E7). Add a subtle "archived" style (grey/badge)? Requires an id→state lookup at render time — deferred.
- **O4 — Archive page hierarchy**: v1 shows roots with a child count, like the Recycle Bin. Is expandable tree browsing inside `/archive` needed, given search already reaches archived children?
- **O5 — Sort/filter on `/archive`**: v1 is `ArchivedAt` desc only. Title sort and label filter could reuse `ApplyFilters` later.
- **O6 — `CLAUDE.md` tsvector statement is stale** (code uses GIN trigram + `ILike` since `20260426140544_SwitchToTrigramSearch`): fix alongside this feature's doc update (§12 step 5), or separately?
