# Feature Specification: Note Properties

Status: Draft for review
Author: smoh (written with Claude)
Date: 2026-07-24
Companion docs: `docs/plannings/note-archive-feature.md`, `docs/recycle-bin-feature.md`
Planning prompt: `docs/prompts/note-properties-planning-prompt.md`

## 1. Overview

Today the only structured metadata a note can carry is the Label system (flat labels, optionally grouped into mutually exclusive categories). That is not expressive enough for a developer workflow: there is no way to attach a due date, a priority number, a done flag, or a typed status to a note, and no way to filter or sort the Home list by such values.

This feature introduces **Note Properties** — Notion-style, typed metadata attached to notes:

- Properties are **defined globally** (name + type + option list for Select kinds); a note carries only **values** for those definitions.
- Six value types in v1: `Text`, `Number`, `Select`, `MultiSelect`, `Date`, `Checkbox`.
- Values are edited in a **property panel** in the editor, between the title and the Tiptap body.
- The Home list gains **property-based filtering and sorting**; the Board gains the same filtering.
- A dedicated `/properties` page manages definitions (create / rename / reorder / change type / manage options / delete).

### 1.1 Why global definitions (decision record)

Three schema models were considered and the **global-definition model was chosen**; ad-hoc (per-note free-form properties) and hybrid (per-note attachment of chosen definitions) were explicitly rejected:

1. **Filter/sort compatibility.** Filtering "Due date before Friday" or sorting by "Priority" only makes sense when every note interprets "Due date" identically — one definition, one type, one option list. Ad-hoc properties make cross-note queries semantically undefined (same name, different types) and force name-based matching.
2. **Single point of management.** Renaming a property or editing a Select option list happens in exactly one place and is instantly consistent across all notes. Ad-hoc models require rename/merge tooling that is out of proportion for a single-user app.
3. **Simplicity of the value store.** Values reference a definition by FK; validation, indexing, and cascade cleanup all hang off that FK.

The trade-off — every definition is potentially visible on every note — is acceptable for a single-user app and is mitigated in the editor panel UX (§9.2: only non-empty values are shown by default) and by the definition cap (§3.1 FR-12).

A note with no value row for a definition simply has an **empty** value for it. Empty is a first-class, filterable state (§7.4).

### Goals

- Attach typed, structured metadata to notes without touching note content or the Tiptap editor.
- Filter and sort the Home list (and filter the Board) by property values, server-side, backed by indexes.
- Manage definitions centrally with safe lifecycle handling (rename, type change, option edits, delete) — no orphaned or mistyped values can survive.
- Decide the long-term relationship with the Label system (§5): coexist now, absorb later.

### Non-goals (v1)

- Extended types: URL, Status (with workflow), Relation (note references), People, Formula, Rollup — open questions only (§13).
- Per-note or per-view visibility configuration of properties (Notion's "hide in view").
- Board grouping by a Select property (board columns) — open question (§13).
- Including property values in full-text search / `ContentText` (§13).
- Exposing properties on the anonymous shared-note page `/share/{token}` (§13).
- Multi-property (stacked) sorting; v1 sorts by at most one property at a time, matching the existing single `sortBy`.
- Label absorption/migration execution — analyzed and recommended in §5, but shipped as a separate follow-up.

## 2. Current Behavior (Baseline)

| Concern | Today |
|---|---|
| Structured metadata | Labels only: `Label` (+ optional `LabelCategory`), join table `NoteLabel` with composite PK `(NoteId, LabelId)` and a `DeletedAt`-matching global query filter. Mutual exclusion within a category is enforced in `LabelService`, not by the DB. |
| Note list filtering | `GET /api/notes?labelIds=` — AND semantics per label; search terms via trigram-backed `ILike` on `Title`/`ContentText`. |
| Note list sorting | `sortBy=date\|title` + `sortDir` only (`NoteService.GetPaged`). Search results are always `UpdatedAt` desc. |
| Board filtering | `GET /api/notes/summaries?labelIds=` — OR semantics. No sorting options (fixed `UpdatedAt` desc). |
| Note metadata writes | Labels: dedicated endpoints (`POST /api/notes/{noteId}/labels`, `DELETE .../labels/{labelId}`), immediate save, archived → 403 via `LabelService.NoteArchivedException`, no `UpdatedAt` bump. |
| Note content writes | `PUT /api/notes/{id}` with DB-level optimistic concurrency on `[ConcurrencyCheck] UpdatedAt`; archived → 403 before the concurrency check; 1500 ms debounced auto-save in `NoteEditor.razor`. |
| Soft delete / archive | `DeletedAt == null` global filter (recycle bin paths opt out via `IgnoreQueryFilters()`); archive uses explicit `ArchivedAt == null` predicates, deliberately **not** a global filter. |
| Account wipe | `AccountService.PurgeAllDataAsync` deletes notes (cascades `NoteLabel`), labels, categories, tokens, settings in one transaction. |

## 3. Requirements

### 3.1 Functional requirements

- **FR-1 Global definitions**: A property definition has a unique name (case-insensitive), a type (one of the six v1 types), a sort order (display order in the panel and management page), and — for `Select`/`MultiSelect` — an ordered option list. Definitions are global; notes never define properties locally.
- **FR-2 Typed values**: A note holds at most one value per definition (composite PK). A value always conforms to its definition's current type; a missing row means "empty". Value validation is server-side (§7.2); the DB schema additionally enforces option↔definition consistency via composite FKs (§4.4).
- **FR-3 Value editing**: Values are edited in an editor property panel (between the title and the body) with type-appropriate MudBlazor controls. Each value change is saved **immediately** via dedicated endpoints — it does not ride the 1500 ms content auto-save (decision and rationale in §7.1).
- **FR-4 Read-only on archive**: All property-value mutations on an archived note return 403 (`ProblemDetails`, mirroring label mutations). Values remain readable. Recycle-bin notes are invisible to value endpoints (global filter → 404); their values are preserved for a lossless restore.
- **FR-5 Filtering (Home + Board)**: `GET /api/notes` and `GET /api/notes/summaries` accept repeatable property-filter parameters (`pf`, grammar in §6.3) with AND semantics across filters, combinable with the existing `search` and `labelIds` parameters. Supported operators per type in §6.3. Filters apply in both the search and non-search branches of `GetPaged`.
- **FR-6 Sorting (Home)**: `GET /api/notes` accepts `sortBy=prop:{definitionId}` (with the existing `sortDir`). Sortable types: `Text`, `Number`, `Date`, `Checkbox`, `Select` (by option sort order). `MultiSelect` is not sortable (400). Notes with an empty value always sort **after** notes with a value, regardless of direction (§7.5). Property sort applies to the non-search branch only; search results stay `UpdatedAt` desc (matches today's rule).
- **FR-7 Definition lifecycle**:
  - Create: 409 on duplicate name (case-insensitive), 400 over the definition cap.
  - Rename / reorder: free at any time; values untouched.
  - **Type change**: allowed only while the definition has **zero values across all notes, including soft-deleted and archived notes** (counted with `IgnoreQueryFilters()`); otherwise 409 with the value count. No conversion matrix in v1 (open question O2).
  - Delete: removes the definition, its options, and **all** value rows (DB cascade — reaches soft-deleted and archived notes' values by construction, §7.6).
- **FR-8 Option lifecycle**: Options exist only on `Select`/`MultiSelect` definitions (400 otherwise). Create/rename: 409 on duplicate name within the definition, 400 over the option cap. Delete while in use is allowed after a usage-count confirmation in the UI; deletion cascades: `Select` values referencing the option are removed entirely, `MultiSelect` selections are removed and value rows left with zero selections are cleaned up (§7.6) — across all notes including soft-deleted/archived.
- **FR-9 Read model**: `GET /api/notes/{id}` returns the note's non-empty values (`Properties`, populated like `Labels`). `NoteSummary` (Home/Board lists) also carries the values so list UIs can render them without N+1 calls.
- **FR-10 Definition management UI**: A new `/properties` page (NavMenu entry) lists definitions with usage counts and provides create / rename / reorder / type change / delete and an option-management dialog. Select pickers in the editor panel additionally allow inline option creation (§9.3).
- **FR-11 Account wipe**: `AccountService.PurgeAllDataAsync` also deletes property definitions (cascading options; value rows are already removed by the notes delete).
- **FR-12 Caps**: max **50** definitions, max **100** options per definition, max **500** characters for a `Text` value, max **20** property filters per request. Rationale: 50 definitions keeps the "Add property" picker and the filter menu scannable and bounds the per-note panel; 100 options bounds Select pickers; 500 chars keeps `TextValue` safely under the PostgreSQL b-tree index entry limit (~2704 bytes) so equality filters stay indexable; 20 filters bounds the number of `EXISTS` subqueries per query. All caps are server-enforced (400) and mirrored client-side.

### 3.2 Non-functional requirements

- **NFR-1** No new external dependencies (EF Core + Npgsql + MudBlazor suffice).
- **NFR-2** Schema changes via one new EF Core migration (`AddNoteProperties`); existing migration files untouched.
- **NFR-3** No `ILIKE` against non-indexed columns. v1 property filters use only b-tree-indexable operators (equality/range); a `contains` operator for `Text` values is deferred until a trigram index on `TextValue` is justified (open question O3).
- **NFR-4** DTOs duplicated between REST and Web projects per convention; no shared project.
- **NFR-5** All code, comments, and UI text in English.
- **NFR-6** Structured Serilog logging for definition/option/value mutations (correlation ID auto-enriched); never log property values themselves at information level (note-content privacy — log IDs and counts only).
- **NFR-7** New tables carry no `UserId`/ownership column (single-user app).
- **NFR-8** `docs/api-specification.md` is updated in the same change as the endpoints.
- **NFR-9** Property read/write paths are async end-to-end (EF Core).
- **NFR-10** Filter/sort queries must be served by indexes (§4.5); no full-table scans on `NotePropertyValues` for a filtered Home page.

## 4. Data Model

### 4.1 Storage model decision: typed-column value table (EAV), not JSONB

Three candidates were compared for storing values:

| Criterion | (A) Value table with typed columns (EAV) | (B) `jsonb` column on `NoteItem` | (C) Hybrid (jsonb + extracted index columns) |
|---|---|---|---|
| Filter query shape | `EXISTS` subquery per filter; plain LINQ, fully translated by EF Core | JSONB path operators; EF Core/Npgsql translation is partial — typed comparisons need raw SQL or `EF.Functions` escape hatches | Same as (B) for reads not covered by extracted columns |
| Range filters & sort (`Number`, `Date`) | B-tree composite indexes `(DefinitionId, <typed column>)` — exact fit | GIN `jsonb_path_ops` serves containment (`@>`), **not** range or ordering; expression indexes per definition would be needed (one index per definition = unmanageable) | Extracted columns re-create (A) anyway |
| Type safety | Enforced by columns + FKs; a `Date` value physically cannot hold text | JSON is stringly-typed; validation only in app code | Mixed |
| Cascade cleanup (definition/option delete) | DB `ON DELETE CASCADE` — reaches soft-deleted/archived notes with zero service code | App-level JSON rewriting across **all** rows incl. recycle bin (`IgnoreQueryFilters()` sweep + full-row rewrites of `NoteItem`) — exactly the class of bug the repo warns about | Partial |
| Unit tests (`Vanadium.Note.REST.Tests`, SQLite provider) | Works — plain relational LINQ | JSONB operators are PostgreSQL-only → whole feature untestable in the suite | Partially untestable |
| Concurrency | Value rows are independent of `NoteItem.UpdatedAt` — no interplay with the editor's optimistic concurrency | Values live inside the note row → every value write races the content auto-save on the `[ConcurrencyCheck]` token | Same as (B) |

**Decision: (A)** — one `NotePropertyValues` table with one nullable column per value kind, plus a `NotePropertySelectedOptions` join table for `MultiSelect`. This mirrors the proven `NoteLabel` pattern (composite PK, matching global filter, DB cascades) and keeps every filter/sort translatable, indexable, and SQLite-testable. The classic EAV drawback (queries touch a tall narrow table) is neutralized at this scale by the composite indexes in §4.5.

Column type choices within (A):

- `NumberValue double` (not `decimal`): the SQLite EF Core provider cannot order by `decimal`, which would exile every Number-sort test from the suite; `double` sorts on both providers and matches Notion's float semantics. Exact-decimal money-style numbers are open question O4.
- `DateValue DateOnly` (maps to PostgreSQL `date`): v1 dates are day-granular; time-of-day (and therefore timezone handling) is deliberately excluded — open question O5.
- `TextValue string` capped at 500 chars (`[MaxLength(500)]`) — see FR-12 rationale.
- `BoolValue bool?` for `Checkbox`; an **absent row means unchecked** (§7.4) so the only stored value is `true` (storing `false` is normalized to row deletion, keeping INV-P1 below).
- `SelectedOptionId Guid?` for `Select`; `MultiSelect` selections live in `NotePropertySelectedOptions`.

### 4.2 Entities

New files under `Vanadium.Note.REST/Models` (file-per-class; Web mirrors per convention where serialized):

```csharp
public enum PropertyType
{
    Text = 0,
    Number = 1,
    Select = 2,
    MultiSelect = 3,
    Date = 4,
    Checkbox = 5
}

public class PropertyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    public PropertyType Type { get; set; }
    /// <summary>Display order in the editor panel, filter menu, and /properties page.</summary>
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PropertyOption> Options { get; set; } = [];
}

public class PropertyOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionId { get; set; }
    [JsonIgnore]
    public PropertyDefinition Definition { get; set; } = null!;
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    /// <summary>Display order in pickers; also the sort key when sorting notes by a Select property.</summary>
    public int SortOrder { get; set; }
}

/// <summary>One value of one property on one note. A missing row = empty value.
/// Exactly the column matching the definition's type is non-null (INV-P1);
/// for MultiSelect the value lives in SelectedOptions and all typed columns stay null.</summary>
public class NotePropertyValue
{
    public Guid NoteId { get; set; }
    public Guid DefinitionId { get; set; }
    [JsonIgnore]
    public NoteItem Note { get; set; } = null!;
    [JsonIgnore]
    public PropertyDefinition Definition { get; set; } = null!;

    [MaxLength(500)]
    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? SelectedOptionId { get; set; }
    [JsonIgnore]
    public PropertyOption? SelectedOption { get; set; }

    public ICollection<NotePropertySelectedOption> SelectedOptions { get; set; } = [];
}

/// <summary>MultiSelect selection row. PK (NoteId, DefinitionId, OptionId).</summary>
public class NotePropertySelectedOption
{
    public Guid NoteId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid OptionId { get; set; }
    [JsonIgnore]
    public NotePropertyValue Value { get; set; } = null!;
    [JsonIgnore]
    public PropertyOption Option { get; set; } = null!;
}
```

`NoteItem` gains a `[NotMapped]` read-model collection, populated like `Labels`:

```csharp
[NotMapped]
public List<NotePropertyValueDto> Properties { get; set; } = [];

[JsonIgnore]
public ICollection<NotePropertyValue> PropertyValues { get; set; } = [];
```

### 4.3 Invariants

- **INV-P1 (row ⇔ non-empty)**: A `NotePropertyValues` row exists **iff** the note has a non-empty value for that definition. Exactly the typed column matching the definition's type is non-null; all others are null. `MultiSelect` rows have ≥ 1 selection row; `Checkbox` rows always hold `BoolValue = true`. Every write path (upsert, clear, option delete cleanup, type change) preserves this, so `empty` / `notempty` filters reduce to `NOT EXISTS` / `EXISTS`.
- **INV-P2 (one value per note × definition)**: enforced by the composite PK `(NoteId, DefinitionId)`.
- **INV-P3 (options belong to their definition)**: a `Select` value's `SelectedOptionId` and every `MultiSelect` selection reference an option of the **same** definition — enforced at the DB level by composite FKs (§4.4), re-checked in the service for friendly 400s.
- **INV-P4 (soft-delete parity)**: `NotePropertyValue` and `NotePropertySelectedOption` carry global query filters matching the note's `DeletedAt == null` filter (mirroring `NoteLabel`), so default queries never see recycle-bin values. **Consequence (repo rule):** any definition-level scan — type-change guard, usage counts, MultiSelect cleanup — must use `IgnoreQueryFilters()` or it will undercount/miss recycle-bin notes' values, breaking lossless restore. Called out per call site in §7.
- **INV-P5 (server-owned)**: property values are never read from the `NoteItem` payload of `POST /api/notes` or `PUT /api/notes/{id}`. Like `ShareToken`/`ArchivedAt`, the collections are `[JsonIgnore]`d / ignored on the write path; the dedicated value endpoints are the only mutation path.

### 4.4 `NoteDbContext` configuration

```csharp
public DbSet<PropertyDefinition> PropertyDefinitions => Set<PropertyDefinition>();
public DbSet<PropertyOption> PropertyOptions => Set<PropertyOption>();
public DbSet<NotePropertyValue> NotePropertyValues => Set<NotePropertyValue>();
public DbSet<NotePropertySelectedOption> NotePropertySelectedOptions => Set<NotePropertySelectedOption>();
```

In `OnModelCreating`:

```csharp
modelBuilder.Entity<NotePropertyValue>()
    .HasKey(v => new { v.NoteId, v.DefinitionId });

modelBuilder.Entity<NotePropertyValue>()
    .HasOne(v => v.Note)
    .WithMany(n => n.PropertyValues)
    .HasForeignKey(v => v.NoteId)
    .OnDelete(DeleteBehavior.Cascade);          // note hard-delete wipes its values

modelBuilder.Entity<NotePropertyValue>()
    .HasOne(v => v.Definition)
    .WithMany()
    .HasForeignKey(v => v.DefinitionId)
    .OnDelete(DeleteBehavior.Cascade);          // definition delete wipes all values (FR-7)

// INV-P3 at the DB level: (DefinitionId, SelectedOptionId) must match an option
// of the same definition. Requires the alternate key below on PropertyOption.
modelBuilder.Entity<NotePropertyValue>()
    .HasOne(v => v.SelectedOption)
    .WithMany()
    .HasForeignKey(v => new { v.DefinitionId, v.SelectedOptionId })
    .HasPrincipalKey(o => new { o.DefinitionId, o.Id })
    .OnDelete(DeleteBehavior.Cascade);          // option delete removes Select value rows (FR-8)

modelBuilder.Entity<NotePropertySelectedOption>()
    .HasKey(s => new { s.NoteId, s.DefinitionId, s.OptionId });

modelBuilder.Entity<NotePropertySelectedOption>()
    .HasOne(s => s.Value)
    .WithMany(v => v.SelectedOptions)
    .HasForeignKey(s => new { s.NoteId, s.DefinitionId })
    .OnDelete(DeleteBehavior.Cascade);          // clearing a value clears its selections

modelBuilder.Entity<NotePropertySelectedOption>()
    .HasOne(s => s.Option)
    .WithMany()
    .HasForeignKey(s => new { s.DefinitionId, s.OptionId })
    .HasPrincipalKey(o => new { o.DefinitionId, o.Id })
    .OnDelete(DeleteBehavior.Cascade);          // option delete removes selections (FR-8)

modelBuilder.Entity<PropertyOption>()
    .HasOne(o => o.Definition)
    .WithMany(d => d.Options)
    .HasForeignKey(o => o.DefinitionId)
    .OnDelete(DeleteBehavior.Cascade);

// INV-P4: mirror the NoteLabel soft-delete parity filters.
modelBuilder.Entity<NotePropertyValue>()
    .HasQueryFilter(v => v.Note.DeletedAt == null);
modelBuilder.Entity<NotePropertySelectedOption>()
    .HasQueryFilter(s => s.Value.Note.DeletedAt == null);
```

Notes:

- The `HasPrincipalKey` calls create an alternate unique index on `PropertyOption (DefinitionId, Id)`; that composite target is what makes INV-P3 a DB guarantee instead of a convention. PostgreSQL accepts the resulting multiple cascade paths (this would need `NO ACTION` juggling on SQL Server, which is not a target).
- Duplicate-name uniqueness for definitions (global) and options (per definition) is enforced **in the service, case-insensitively**, matching the existing `LabelService` precedent — no DB unique index on names.
- No change to the `NoteItem` global filter; archive stays predicate-based (per the archive design decision — nothing here folds `ArchivedAt` into any filter).

### 4.5 Indexes

```csharp
// One composite index per filter/sort shape: "for definition D, compare/order <typed column>".
modelBuilder.Entity<NotePropertyValue>()
    .HasIndex(v => new { v.DefinitionId, v.NumberValue });
modelBuilder.Entity<NotePropertyValue>()
    .HasIndex(v => new { v.DefinitionId, v.DateValue });
modelBuilder.Entity<NotePropertyValue>()
    .HasIndex(v => new { v.DefinitionId, v.TextValue });
modelBuilder.Entity<NotePropertyValue>()
    .HasIndex(v => new { v.DefinitionId, v.SelectedOptionId });
modelBuilder.Entity<NotePropertyValue>()
    .HasIndex(v => new { v.DefinitionId, v.BoolValue });

// Option usage counts and option-delete fan-out.
modelBuilder.Entity<NotePropertySelectedOption>()
    .HasIndex(s => s.OptionId);
```

- The PK `(NoteId, DefinitionId)` already serves the per-note panel read and the `EXISTS (… v.NoteId == n.Id && v.DefinitionId == D)` probe direction.
- `empty`/`notempty` filters and the `Checkbox` false-includes-missing semantics are `NOT EXISTS` probes against the PK — no extra index needed.
- The GIN trigram indexes on `NoteItem` are untouched; no property column gets an `ILIKE` in v1 (NFR-3).

### 4.6 Migration

- `dotnet ef migrations add AddNoteProperties --project Vanadium.Note.REST` — creates the four tables, FKs, alternate key, filters (query filters are model-level, not schema), and indexes above.
- No backfill: existing notes simply have no value rows (all empty).
- No changes to existing tables — `NoteItem` gains only unmapped/navigation members.

## 5. Label System: Absorb or Coexist

Labels are conceptually a degenerate property system: a `LabelCategory` ≈ a `Select` definition (mutual exclusion within a category ≡ single-select), the pool of uncategorized labels ≈ options of one `MultiSelect` definition. Two paths were compared:

| Criterion | (a) Absorb labels into properties | (b) Keep both in parallel |
|---|---|---|
| Conceptual integrity | One metadata system; `/properties` is the single management surface | Two overlapping systems; users must decide "label or property?" per use case |
| v1 scope & risk | Adds data migration + removal/rewrite of Label API, `LabelPicker`, Home/Board label filter UI, board label chips, `includeLabels` plumbing — roughly doubles the release | Property feature ships standalone; label surface untouched |
| Data migration | Mechanical and lossless (§5.1) but must run inside the same migration window as the API removal | None |
| Long-term cost | None after cutover | Permanent duplication (two filter UIs, two mutual-exclusion mechanisms, `OrderLabelsForDisplay` vs panel ordering) |
| Rollback story | Hard once Label tables are dropped | Trivial |

**Recommendation: (b) for v1, (a) as a planned follow-up release.** Ship properties alongside labels, let the property UX stabilize (panel ergonomics, filter bar, option management), then execute the absorption as its own change with its own migration. Absorption is deliberately **out of v1 scope** but the target shape is fixed now so v1 makes no decision that blocks it.

### 5.1 Absorption sketch (follow-up release, recorded for planning)

1. Migration (data, inside `AddLabelAbsorption`):
   - Each `LabelCategory` → a `Select` `PropertyDefinition` (same name, options = its labels, option order alphabetical to match today's display).
   - All uncategorized labels → one `MultiSelect` definition named `Labels`, one option per label.
   - Each `NoteLabel` row → a `NotePropertyValue` (+ selection row for the MultiSelect case). Category labels map to `SelectedOptionId`; mutual exclusion is preserved structurally because a note has at most one label per category today (server-enforced) — the migration takes the newest `NoteLabel` on the (theoretically impossible) duplicate.
   - Name collisions with existing property definitions get a ` (Labels)` suffix.
2. Remove `LabelsController`, `LabelService`, `Label`/`LabelCategory`/`NoteLabel` entities + tables (follow-up migration drops them), `labelIds` query parameters, `LabelPicker.razor`, label chips (replaced by property chips), `PagedResult.Labels`/`includeLabels`.
3. `docs/api-specification.md`: label endpoints removed, properties become the only metadata API.
4. Recycle-bin nuance: the `NoteLabel` → value migration must copy rows for soft-deleted notes too (plain SQL in the migration operates below EF filters, so this is automatic — but the verification checklist must include a restore-after-migration test).

v1 impact of this decision: none in code; §13 O1 tracks the absorption timing.

## 6. API Design

All new owner endpoints require the standard JWT/PAT auth. A new `PropertiesController` hosts definition, option, and note-value routes (mirroring how `LabelsController` hosts both label CRUD and note-label assignment).

### 6.1 New endpoints

| Method & route | Behavior | Responses |
|---|---|---|
| `GET /api/properties?includeUsage=` | All definitions with options, ordered by `SortOrder` then `Name`. `includeUsage=true` adds per-definition `ValueCount` and per-option `NoteCount` (counted with `IgnoreQueryFilters()` — includes recycle-bin and archived notes; used by the management page and delete dialogs). | 200 `List<PropertyDefinitionDto>` |
| `POST /api/properties` | Create definition. Duplicate name (case-insensitive) → 409; > 50 definitions → 400; invalid type → 400. | 201 / 400 / 409 |
| `PUT /api/properties/{id}` | Update name / sort order / type. Type change with existing values (counted via `IgnoreQueryFilters()`) → 409 with the count in `ProblemDetails.detail`. Changing type away from Select kinds (with zero values) deletes the now-meaningless options. | 200 / 400 / 404 / 409 |
| `DELETE /api/properties/{id}` | Delete definition; DB cascades options, all value rows, all selection rows (incl. soft-deleted/archived notes' rows). | 204 / 404 |
| `POST /api/properties/{id}/options` | Add option. Non-Select definition → 400; duplicate name in definition → 409; > 100 options → 400. | 201 / 400 / 404 / 409 |
| `PUT /api/properties/{id}/options/{optionId}` | Rename / reorder option. | 200 / 400 / 404 / 409 |
| `DELETE /api/properties/{id}/options/{optionId}` | Delete option; cascades per FR-8 + MultiSelect empty-row cleanup (§7.6). | 204 / 404 |
| `PUT /api/notes/{noteId}/properties/{definitionId}` | Upsert one value (§7.2). Archived note → 403; recycle-bin/unknown note or unknown definition → 404; type-mismatched or invalid payload → 400. Returns the stored value. | 200 / 400 / 403 / 404 |
| `DELETE /api/notes/{noteId}/properties/{definitionId}` | Clear one value (delete the row). Missing row → 204 (idempotent). Archived → 403. | 204 / 403 / 404 |

### 6.2 Modified endpoints

| Method & route | Change |
|---|---|
| `GET /api/notes` | New repeatable `pf` filter parameter (§6.3) applied in **both** search and non-search branches; new `sortBy=prop:{definitionId}` (non-search branch only). Malformed `pf`/`sortBy` → 400 `ProblemDetails`. |
| `GET /api/notes/summaries` | Same `pf` parameter (Board filtering). AND semantics across `pf` entries (note: `labelIds` on this endpoint keeps its existing OR semantics — documented asymmetry). |
| `GET /api/notes/{id}` | Response `NoteItem` now carries `Properties` (non-empty values, ordered by definition `SortOrder`). |
| `POST /api/notes`, `PUT /api/notes/{id}` | **No payload change.** Property collections are ignored on the write path (INV-P5); `docs/api-specification.md` states this explicitly. |
| `GET /api/share/{token}` | Unchanged — `SharedNote` does **not** expose properties in v1 (open question O7). |

### 6.3 Property filter grammar (`pf`)

Repeatable query parameter, AND semantics, max 20 (FR-12):

```text
pf={definitionId}:{op}[:{value}]
```

- `definitionId` — GUID of a property definition (unknown → 400).
- `value` — URL-encoded; omitted for `empty` / `notempty`. For `anyof`, a comma-separated list of option GUIDs.
- Malformed triplet, unknown op, op/type mismatch, or unparsable value → 400 `ProblemDetails` (fail fast; never silently ignore a filter).

Operators per type:

| Type | Operators | Value format | Semantics |
|---|---|---|---|
| `Text` | `eq`, `ne`, `empty`, `notempty` | literal string (≤ 500 chars) | Exact match, case-sensitive (b-tree). No `contains` in v1 (NFR-3, O3). `ne` matches notes whose value exists and differs (empty notes are NOT matched — use `empty`). |
| `Number` | `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `empty`, `notempty` | invariant-culture double | Range ops combine: two `pf` entries on the same definition express *between*. |
| `Date` | `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `empty`, `notempty` | `yyyy-MM-dd` | Same combination rule for ranges. |
| `Checkbox` | `eq` | `true` / `false` | `eq:true` → value row with `BoolValue = true` exists; `eq:false` → **no such row** (missing = unchecked, §7.4). `empty`/`notempty` are rejected for Checkbox (400) — two-state by design. |
| `Select` | `eq`, `ne`, `anyof`, `empty`, `notempty` | option GUID(s) | `eq` = has that option; `anyof` = has one of the listed options; options must belong to the definition (400). |
| `MultiSelect` | `eq`, `anyof`, `empty`, `notempty` | option GUID(s) | `eq` = the option is among the selections; `anyof` = at least one listed option is selected. (`allof` deferred — O6.) |

Examples:

```text
GET /api/notes?pf=8f…d2:eq:true                       # Checkbox "Done" checked
GET /api/notes?pf=3a…91:gte:2026-07-01&pf=3a…91:lt:2026-08-01   # Date in July 2026
GET /api/notes?pf=6c…07:anyof:opt1,opt2&sortBy=prop:44…be&sortDir=asc
```

### 6.4 DTOs

REST project (`Vanadium.Note.REST/Models`), mirrored one-to-one in `Vanadium.Note.Web/Models` per convention:

```csharp
public class PropertyDefinitionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PropertyType Type { get; set; }
    public int SortOrder { get; set; }
    public List<PropertyOptionDto> Options { get; set; } = [];
    /// <summary>Only populated when includeUsage=true. Counted with IgnoreQueryFilters().</summary>
    public int? ValueCount { get; set; }
}

public class PropertyOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>Only populated when includeUsage=true.</summary>
    public int? NoteCount { get; set; }
}

public record CreatePropertyDefinitionRequest(
    [Required][MaxLength(100)] string Name,
    PropertyType Type);

public record UpdatePropertyDefinitionRequest(
    [Required][MaxLength(100)] string Name,
    PropertyType Type,
    int SortOrder);

public record CreatePropertyOptionRequest([Required][MaxLength(100)] string Name);

public record UpdatePropertyOptionRequest(
    [Required][MaxLength(100)] string Name,
    int SortOrder);

/// <summary>Exactly the member(s) matching the definition's type must be set (§7.2).</summary>
public class SetNotePropertyValueRequest
{
    [MaxLength(500)]
    public string? TextValue { get; set; }      // Text
    public double? NumberValue { get; set; }    // Number
    public DateOnly? DateValue { get; set; }    // Date
    public bool? BoolValue { get; set; }        // Checkbox
    public Guid? OptionId { get; set; }         // Select
    public List<Guid>? OptionIds { get; set; }  // MultiSelect
}

/// <summary>Read model for one non-empty value; embedded in NoteItem.Properties and NoteSummary.Properties.</summary>
public class NotePropertyValueDto
{
    public Guid DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;   // denormalized for display
    public PropertyType Type { get; set; }
    public string? TextValue { get; set; }
    public double? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? OptionId { get; set; }
    public List<Guid> OptionIds { get; set; } = [];
}
```

`NoteSummary` (both projects) gains:

```csharp
/// <summary>Non-empty property values, ordered by definition SortOrder. Drives list chips
/// (v1 renders only the active sort property's value, §9.4) and avoids N+1 value fetches.</summary>
public List<NotePropertyValueDto> Properties { get; set; } = [];
```

## 7. Processing Flows

### 7.1 Save model: dedicated immediate saves, decoupled from content auto-save (decision record)

Property value writes go through the dedicated endpoints in §6.1 and are saved **immediately when a control commits** (checkbox toggle, option pick, date pick, number/text blur or Enter) — they do **not** ride the note's 1500 ms debounced `PUT /api/notes/{id}`. Rationale:

1. **Concurrency isolation.** `NoteItem.UpdatedAt` is the `[ConcurrencyCheck]` token for content saves. If values traveled inside `PUT /api/notes/{id}`, every property tweak would advance the token and every open editor's next auto-save would 409 — or worse, a force-save would silently clobber values. Dedicated endpoints sidestep the token entirely.
2. **Consistency with the closest precedent.** Label add/remove already works exactly this way (immediate, dedicated route, archived → 403, no `UpdatedAt` bump).
3. **Server-owned write surface (INV-P5).** Keeping values out of the `NoteItem` payload preserves the existing rule that lifecycle/metadata fields are never client-postable through create/update.

**Property saves do not bump `NoteItem.UpdatedAt`** — same reasoning as `SetShare` (metadata must not reorder date-sorted lists) plus the token-poisoning issue in (1). Consequence: a property-only edit does not float the note in the default sort; accepted, consistent with labels, revisit in O8.

Per-value writes are last-write-wins (no per-value concurrency token) — acceptable for a single-user app where the realistic race is two of the owner's own tabs.

### 7.2 Value upsert with type validation

```text
UpsertValue(noteId, definitionId, req):
  note = db.Notes.Select(n => new { n.ArchivedAt }).FirstOrDefault(n => n.Id == noteId)
         // global filter → recycle-bin note invisible → 404
  if note == null                → 404
  if note.ArchivedAt != null     → 403 NoteArchivedException (mirrors LabelService)

  def = db.PropertyDefinitions.Include(Options).FirstOrDefault(d => d.Id == definitionId)
  if def == null                 → 404

  // Exactly the member for def.Type must be provided; all others must be null (400 otherwise).
  switch def.Type:
    Text:        require req.TextValue != null (after Trim; "" → 400 "use DELETE to clear"); len ≤ 500
    Number:      require req.NumberValue != null && double.IsFinite(value)
    Date:        require req.DateValue != null            // DateOnly is inherently valid
    Checkbox:    require req.BoolValue != null
                 if req.BoolValue == false → treat as clear: delete row if present, return empty (INV-P1)
    Select:      require req.OptionId != null && def.Options contains it   // else 400
    MultiSelect: require req.OptionIds != null && non-empty && distinct
                 && all ∈ def.Options                                       // else 400
                 (empty list → treat as clear, like Checkbox=false)

  row = db.NotePropertyValues.Find(noteId, definitionId)   // filtered set is fine: note is not deleted
  if row == null: row = new(noteId, definitionId); db.Add(row)
  null out ALL typed columns, then set only the one for def.Type   // heals any drift, keeps INV-P1
  if MultiSelect: replace row.SelectedOptions with req.OptionIds (add missing, remove absent)
  SaveChanges                                              // NoteItem untouched — no UpdatedAt bump
  log Information "Property {DefinitionId} set on note {NoteId}" (no value logged, NFR-6)
  return dto
```

Clear (`DELETE .../properties/{definitionId}`) is the same guard sequence, then: delete the row if present (selections cascade), 204 either way.

### 7.3 Filter construction (`NoteService.ApplyPropertyFilters`)

Parsing happens in the controller (`pf` strings → `List<PropertyFilter>` records); semantic validation and query building in the service, so unit tests can call it directly:

```text
record PropertyFilter(Guid DefinitionId, PropertyFilterOp Op, string? RawValue)

ApplyPropertyFilters(query, filters):
  if filters.Count > 20 → 400
  defs = load referenced definitions in one query; unknown id → 400
  foreach f (AND — chained Where):
    validate op against defs[f].Type (table §6.3) and parse RawValue → 400 on mismatch
    switch (type, op):
      Text eq        → query.Where(n => n.PropertyValues.Any(v => v.DefinitionId == D && v.TextValue == s))
      Number gt      → …Any(v => v.DefinitionId == D && v.NumberValue > x)
      Date lte       → …Any(v => v.DefinitionId == D && v.DateValue <= d)
      Checkbox true  → …Any(v => v.DefinitionId == D && v.BoolValue == true)
      Checkbox false → !…Any(v => v.DefinitionId == D && v.BoolValue == true)   // missing = unchecked
      Select eq      → …Any(v => v.DefinitionId == D && v.SelectedOptionId == o)
      Select anyof   → …Any(v => v.DefinitionId == D && opts.Contains(v.SelectedOptionId!.Value))
      MultiSelect eq → n.PropertyValues.Any(v => v.DefinitionId == D
                                              && v.SelectedOptions.Any(s => s.OptionId == o))
      MultiSelect anyof → …SelectedOptions.Any(s => opts.Contains(s.OptionId))
      * notempty     → …Any(v => v.DefinitionId == D)          // INV-P1 makes this sufficient
      * empty        → !…Any(v => v.DefinitionId == D)
```

Each predicate translates to an `EXISTS` subquery served by the `(DefinitionId, <typed column>)` indexes (§4.5). The navigations run through the value-table global filters, so recycle-bin values can never match (correct: those notes are excluded from the outer query anyway).

`ApplyPropertyFilters` is called from `GetPaged` (both branches, alongside `ApplyFilters`) and `GetAllSummaries`.

### 7.4 Empty semantics summary

| Situation | Representation | Filter behavior |
|---|---|---|
| Value never set / cleared | no row | matches `empty`; excluded by every comparison op |
| Checkbox unchecked | no row (writes of `false` normalize to delete) | matches `Checkbox eq:false` |
| Checkbox checked | row with `BoolValue = true` | matches `Checkbox eq:true`, `notempty` |
| MultiSelect with zero selections | impossible (INV-P1 — normalized to row deletion) | — |

### 7.5 Property sort construction (`GetPaged`, non-search branch)

`sortBy=prop:{definitionId}` → resolve the definition (unknown → 400; `MultiSelect` → 400). Empties always sort last via a presence key, independent of `sortDir` (a Home list led by 40 valueless notes when sorting by "Priority desc" is useless):

```text
sub(n) = n.PropertyValues.Where(v => v.DefinitionId == D)
                         .Select(v => <nullable typed selector>)
                         .FirstOrDefault()        // null when no row
  Text:     v.TextValue                 (string?, ordinal — culture-specific collation is O9)
  Number:   (double?)v.NumberValue
  Date:     (DateOnly?)v.DateValue
  Checkbox: (bool?)v.BoolValue
  Select:   (int?)v.SelectedOption!.SortOrder     // option order, Notion-style

ordered = query.OrderBy(n => sub(n) == null ? 1 : 0)      // presence first: empties last
               .ThenBy/ThenByDescending(n => sub(n))       // sortDir
               .ThenByDescending(n => n.UpdatedAt)          // stable tiebreak
```

EF translates the correlated `FirstOrDefault` into a scalar subquery; the `(DefinitionId, <typed column>)` index serves it per row over the already-paged candidate set. The existing `sortBy=date|title` paths are untouched; search results remain forced to `UpdatedAt` desc.

### 7.6 Definition/option lifecycle flows

```text
ChangeDefinition(id, req):                       // PUT /api/properties/{id}
  def = find or 404
  if req.Name differs (case-insensitive) and another def has it → 409
  if req.Type != def.Type:
      count = db.NotePropertyValues.IgnoreQueryFilters()      // INV-P4: include recycle-bin values
                .Count(v => v.DefinitionId == id)
      if count > 0 → 409 ProblemDetails
          "Cannot change the type of a property that has values on {count} note(s). Clear the values first."
      if def.Type was Select/MultiSelect and req.Type is not → db.RemoveRange(def.Options)
  apply name/type/sortOrder; SaveChanges

DeleteDefinition(id):                            // DELETE /api/properties/{id}
  find or 404; db.Remove(def); SaveChanges
  // DB cascade removes options → value rows → selection rows, INCLUDING rows belonging to
  // soft-deleted and archived notes: cascades run below EF's query filters, so no
  // IgnoreQueryFilters() sweep is needed here — this is the payoff of storage model (A).

DeleteOption(defId, optionId):                   // DELETE /api/properties/{defId}/options/{optionId}
  find or 404 (must belong to defId); db.Remove(option); SaveChanges
  // Cascades: Select value rows referencing it are deleted (composite FK);
  //           MultiSelect selection rows are deleted.
  // Cleanup (INV-P1): MultiSelect value rows left with zero selections must go —
  // and must include recycle-bin notes' rows, or a restored note resurrects a
  // phantom "notempty" value:
  if def.Type == MultiSelect:
      db.NotePropertyValues.IgnoreQueryFilters()
        .Where(v => v.DefinitionId == defId && !v.SelectedOptions.Any())
        .ExecuteDelete()          // navigation inside IgnoreQueryFilters sees unfiltered selections
  // UI precondition: the confirm dialog shows usage from GET /api/properties?includeUsage=true.
```

Usage counting (management page, delete dialogs):

```text
ValueCount(defId)  = db.NotePropertyValues.IgnoreQueryFilters().Count(v => v.DefinitionId == defId)
NoteCount(optId)   = db.NotePropertyValues.IgnoreQueryFilters().Count(v => v.SelectedOptionId == optId)
                   + db.NotePropertySelectedOptions.IgnoreQueryFilters().Count(s => s.OptionId == optId)
// IgnoreQueryFilters throughout: a count that omits recycle-bin notes would let the user
// "safely" delete an option that a restore then silently misses. Archived notes are visible
// to default queries anyway (no archive global filter) — no extra handling needed.
```

### 7.7 Note lifecycle interactions (no new code paths)

- **Soft delete / restore**: value rows are untouched (they key off `NoteId` only); the global filters hide them while deleted. Restore is automatically lossless. No `IgnoreQueryFilters()` changes to `NoteService.Delete/Restore`.
- **Hard delete (purge, permanent delete, empty recycle bin)**: `NoteId` FK cascade removes value + selection rows. No change to `HardDeleteAsync`.
- **Archive / unarchive**: no value changes; reads stay available (archive has no global filter), writes blocked by the §7.2 guard.
- **Account wipe** (`AccountService.PurgeAllDataAsync`): add, after the labels/categories deletes:

```csharp
var propertyDefsRemoved = await db.PropertyDefinitions.ExecuteDeleteAsync(ct);
// Options cascade; note-value rows were already removed by the Notes delete above.
```

### 7.8 Tiptap / JS interop impact

**None — confirmed.** The property panel is a pure Blazor component rendered above the `tiptap-{guid}` container div; it never touches editor content, `tiptapInterop.*`, or `wwwroot/js/tiptap-editor.js`. Values live outside `Content`, so `StripHtml`/`ContentText`, the serialization hard rule (user-visible text in element text content), orphan-file scanning, and the toggle/fold machinery are all unaffected. The read-only editor mode (`tiptapInterop.setEditable`) stays as-is; the panel independently disables its controls off the same `note.ArchivedAt != null` flag.

## 8. Interface Design

### 8.1 Backend changes by file

| File | Change |
|---|---|
| `Models/PropertyType.cs`, `Models/PropertyDefinition.cs`, `Models/PropertyOption.cs`, `Models/NotePropertyValue.cs`, `Models/NotePropertySelectedOption.cs` | New entities (§4.2), file-per-class |
| `Models/PropertyDefinitionDto.cs`, `Models/PropertyOptionDto.cs`, `Models/NotePropertyValueDto.cs`, `Models/PropertyRequests.cs` | New DTOs / request records (§6.4) |
| `Models/NoteItem.cs` | Add `[NotMapped] Properties` + `[JsonIgnore] PropertyValues` navigation |
| `Models/NoteSummary.cs` | Add `Properties` list |
| `Data/NoteDbContext.cs` | Four DbSets, keys/FKs/alternate key, INV-P4 query filters, indexes (§4.4–4.5). **`NoteItem` global filter unchanged.** |
| `Migrations/` | New `AddNoteProperties` migration |
| `Services/PropertyService.cs` | New service: definition/option CRUD, usage counts, value upsert/clear (§7.2, §7.6); nested `NoteArchivedException` mirroring `LabelService` |
| `Services/NoteService.cs` | `ApplyPropertyFilters` (§7.3), property sort in `GetPaged` (§7.5), `pf` support in `GetAllSummaries`, `Properties` projection into `NoteSummary`/`Get` (populate like `PopulateLabels`) |
| `Services/AccountService.cs` | `PropertyDefinitions.ExecuteDeleteAsync` in the wipe transaction (§7.7) |
| `Controllers/PropertiesController.cs` | New controller: all §6.1 routes, `pf`-style ProblemDetails mappings, structured logs |
| `Controllers/NotesController.cs` | Parse/validate `pf` + `sortBy=prop:` into service inputs; 400 mappings |
| `docs/api-specification.md` | All §6.1/§6.2 changes, same commit (NFR-8) |

New REST service signatures (draft):

```csharp
public class PropertyService(NoteDbContext db, ILogger<PropertyService> logger)
{
    public class NoteArchivedException() : InvalidOperationException("Note is archived and read-only.");

    // Definitions
    public Task<List<PropertyDefinitionDto>> GetAllAsync(bool includeUsage, CancellationToken ct = default);
    public Task<PropertyDefinitionDto> CreateAsync(string name, PropertyType type, CancellationToken ct = default);            // InvalidOperationException → 409, cap → ArgumentException/400
    public Task<PropertyDefinitionDto?> UpdateAsync(Guid id, string name, PropertyType type, int sortOrder, CancellationToken ct = default); // null → 404; TypeChangeBlockedException → 409
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    // Options
    public Task<PropertyOptionDto?> AddOptionAsync(Guid definitionId, string name, CancellationToken ct = default);
    public Task<PropertyOptionDto?> UpdateOptionAsync(Guid definitionId, Guid optionId, string name, int sortOrder, CancellationToken ct = default);
    public Task<bool> DeleteOptionAsync(Guid definitionId, Guid optionId, CancellationToken ct = default);   // incl. MultiSelect empty-row cleanup

    // Note values
    public Task<NotePropertyValueDto?> SetValueAsync(Guid noteId, Guid definitionId, SetNotePropertyValueRequest req, CancellationToken ct = default); // null → 404; NoteArchivedException → 403; ValidationException → 400
    public Task<bool> ClearValueAsync(Guid noteId, Guid definitionId, CancellationToken ct = default);       // NoteArchivedException → 403
}
```

`NoteService` additions:

```csharp
private static IQueryable<NoteItem> ApplyPropertyFilters(
    IQueryable<NoteItem> query, IReadOnlyList<PropertyFilter> filters,
    IReadOnlyDictionary<Guid, PropertyDefinition> definitions);

// GetPaged / GetAllSummaries signatures gain: IReadOnlyList<PropertyFilter>? propertyFilters
// GetPaged additionally resolves sortBy "prop:{guid}" before building the ordering (§7.5).
```

### 8.2 Frontend changes by file

| File | Change |
|---|---|
| `Models/*` (Web) | Mirror all new DTOs/enums (§6.4) per convention |
| `Services/PropertyService.cs` (Web) | New HTTP client following the existing `ServiceResult<T>` pattern (below) |
| `Services/NoteService.cs` (Web) | `GetNotesAsync`/`GetSummariesAsync` gain property filter + property sort parameters (serialize to `pf` / `sortBy=prop:{id}`) |
| `Components/NotePropertyPanel.razor` | Editor panel between title and Tiptap container (§9.2) |
| `Components/PropertyValueEditor.razor` | One value control, switched on `PropertyType` (§9.2 control table) |
| `Components/PropertyFilterBar.razor` | Filter chip bar + "Add filter" menu for Home and Board (§9.4) |
| `Components/PropertyOptionsDialog.razor` | Option management dialog for the `/properties` page |
| `Pages/Properties.razor` | New `/properties` management page (§9.5) |
| `Pages/Home.razor` | Mount `PropertyFilterBar`; property entries in the sort menu; sort-property value chip on rows (§9.4) |
| `Pages/Board.razor` | Mount `PropertyFilterBar` (filtering only in v1) |
| `Pages/NoteEditor.razor` | Mount `NotePropertyPanel` under the title field; pass `IsReadOnly = note.ArchivedAt != null`. **No `tiptapInterop` changes (§7.8).** |
| `Layout/NavMenu.razor` | "Properties" nav item (e.g. between Board and Archive) |

New Web `PropertyService` client methods:

```csharp
public Task<ServiceResult<List<PropertyDefinitionDto>>> GetDefinitionsAsync(bool includeUsage = false);
public Task<ServiceResult<PropertyDefinitionDto>> CreateDefinitionAsync(CreatePropertyDefinitionRequest req);
public Task<ServiceResult<PropertyDefinitionDto>> UpdateDefinitionAsync(Guid id, UpdatePropertyDefinitionRequest req);
public Task<ServiceResult<bool>> DeleteDefinitionAsync(Guid id);
public Task<ServiceResult<PropertyOptionDto>> AddOptionAsync(Guid definitionId, CreatePropertyOptionRequest req);
public Task<ServiceResult<PropertyOptionDto>> UpdateOptionAsync(Guid definitionId, Guid optionId, UpdatePropertyOptionRequest req);
public Task<ServiceResult<bool>> DeleteOptionAsync(Guid definitionId, Guid optionId);
public Task<ServiceResult<NotePropertyValueDto>> SetValueAsync(Guid noteId, Guid definitionId, SetNotePropertyValueRequest req);
public Task<ServiceResult<bool>> ClearValueAsync(Guid noteId, Guid definitionId);
```

Definitions are fetched once per circuit and cached in the Web `PropertyService` (invalidated after any definition/option mutation) — the panel, filter bar, and sort menu all read from that cache.

## 9. User Scenarios / UX

### 9.1 End-to-end scenario: from definition to a filtered list

1. User opens `/properties` → "New property" → name `Priority`, type `Number`. Creates `Due`, type `Date`; `Done`, type `Checkbox`; `Status`, type `Select` with options `Todo` / `Doing` / `Blocked`.
2. In the editor, the panel under the title shows "Add property". User adds `Priority` = 1, picks `Due` = 2026-08-01, checks `Done` off, sets `Status` = `Doing`. Each commit saves instantly (no explicit save button; a brief saved indicator on the panel).
3. On Home, the user adds filters `Done is false` and `Due before 2026-08-01`, sorts by `Priority` ascending. The list shows matching notes with a small `Priority: n` chip; the URL query reflects the filters so a refresh keeps them.
4. On Board, the same filter bar narrows cards; card layout is otherwise unchanged in v1.

### 9.2 Editor property panel

- Rendered between the title `MudTextField` and the Tiptap container; collapsible (`MudCollapse`) with a header row `Properties (n)`; collapsed state kept in-memory per session (not persisted in v1).
- **Shows non-empty values only, ordered by definition `SortOrder`**, each as `name : control`. An **"Add property"** button opens a picker of the remaining (empty) definitions. Clearing a value returns the definition to the picker. Decision record: with global definitions and a 50-definition cap, always showing all definitions would bloat every note; showing set-values-only mirrors Notion pages and keeps small notes small. A "show all empty properties" expander is O10.
- Controls per type (MudBlazor only, NFR-1):

| Type | Control | Commit trigger |
|---|---|---|
| `Text` | `MudTextField<string>` | blur / Enter |
| `Number` | `MudNumericField<double?>` | blur / Enter |
| `Select` | `MudAutocomplete<PropertyOptionDto>` (options + "Create '{text}'" row → inline `POST .../options`, §9.3) | selection |
| `MultiSelect` | `MudSelect<Guid>` `MultiSelection=true` with chips (+ same inline create row) | picker close |
| `Date` | `MudDatePicker` (value mapped to `DateOnly`, displayed ISO `yyyy-MM-dd`) | pick |
| `Checkbox` | `MudCheckBox<bool>` | toggle |

- Each commit calls `SetValueAsync`/`ClearValueAsync`; failures surface as a snackbar and the control reverts to the last known server value. 403 (archived elsewhere) additionally flips the whole editor into its read-only mode, reusing the existing archived-note handling.
- Read-only (archived) notes: panel renders values as plain text/chips, controls disabled, "Add property" hidden — consistent with the disabled title and non-editable Tiptap body.

### 9.3 Inline option creation

Typing an unknown value into a `Select`/`MultiSelect` picker offers `Create "{text}"`; picking it calls `POST /api/properties/{id}/options` then immediately assigns the new option. Duplicate (race with another tab) → the 409 is swallowed by re-fetching definitions and assigning the existing option. Cap overflow (100) → snackbar with the server message.

### 9.4 Home filtering & sorting

- `PropertyFilterBar` above the list: active filters as removable `MudChip`s (`Due < 2026-08-01`), "Add filter" menu = definition list → operator → value input (same type-switched controls). Filters AND with each other and with the existing search box and label filter.
- Sort menu gains a "Property" group listing sortable definitions (MultiSelect entries hidden). Selecting one issues `sortBy=prop:{id}`.
- When a property sort is active, each row renders that property's value as a trailing chip (data already present in `NoteSummary.Properties`); no other property chips on rows in v1 (O10 considers richer row chips).
- Filter and sort state is serialized into the Home page URL query (same mechanism as the existing search/label state) so refresh/back preserve it.
- Search + property filters compose: searching narrows by text while `pf` filters still apply (archived notes can appear per the existing search rule, with their badge; property sort is ignored during search per FR-6).

### 9.5 `/properties` management page

- `MudTable` of definitions: name, type (readable label), usage count (`includeUsage=true`), option count, sort-order drag or up/down controls.
- Row actions: **Rename** (inline dialog), **Change type** (dialog listing the six types; on 409 shows "in use on N notes — clear values first"), **Options…** (for Select kinds → `PropertyOptionsDialog`: add/rename/reorder/delete options; option delete confirm shows `NoteCount`: "Used on N note(s) (including archived and recycle-bin notes). Remove?"), **Delete** (confirm shows `ValueCount`: "This will remove its values from N note(s), including archived and recycle-bin notes. This cannot be undone.").
- Empty state: "No properties yet. Create one to attach structured data — like a due date or a priority — to your notes."

### 9.6 Use cases (summary)

| # | Actor goal | Flow | Outcome |
|---|---|---|---|
| UC-1 | Track task state on notes | Create `Status` Select + `Done` Checkbox → set values in editor | Values visible in panel; filterable on Home/Board |
| UC-2 | See what is due this week | Home → filters `Due gte {mon}` + `Due lte {sun}` → sort by `Due` asc | Filtered, sorted list; due-date chip per row |
| UC-3 | Evolve an option list | Editor picker → `Create "Blocked"`; later rename it on `/properties` | Option available everywhere instantly; rename reflects on all notes (stored by ID) |
| UC-4 | Retire a property | `/properties` → Delete → confirm with usage count | Definition + all values gone everywhere, incl. archive/recycle bin |
| UC-5 | Read an archived note's metadata | Open archived note | Panel read-only; any bypassing write gets 403 |
| UC-6 | (Follow-up release) Migrate labels | Run label absorption (§5.1) | Categories → Select properties; label UI retired |

## 10. Edge Cases

- **E1 — Value write to an archived note**: 403 with `ProblemDetails` from every value endpoint (§7.2 guard runs before anything else, mirroring `LabelService`). A panel open in a second tab when the note is archived elsewhere gets 403 on its next commit → editor flips to read-only mode (no concurrency dialog — same UX rule as the archive feature's E5).
- **E2 — Value write to a recycle-bin note**: the global filter hides the note → 404. Values of soft-deleted notes are retained untouched and reappear on restore (FR-4).
- **E3 — Definition deleted while a note panel is open**: the next value commit returns 404 (definition gone); the client re-fetches definitions, drops the row from the panel, and shows "This property was deleted."
- **E4 — Option deleted while selected in an open picker**: commit returns 400 (option not in definition); same recovery — refresh definitions, revert control.
- **E5 — Type change race**: user A changes `Priority` Number→Text (allowed, zero values); user B's tab still shows a numeric control and commits `NumberValue` → 400 type-mismatch → client refreshes definitions and re-renders the control. No corrupt value can be stored (validation is server-side against the *current* type).
- **E6 — Type change with values hiding in the recycle bin**: guard counts with `IgnoreQueryFilters()` (§7.6), so values on soft-deleted notes block the change too — otherwise a restore would resurrect a value whose column no longer matches the definition's type, breaking INV-P1.
- **E7 — Option delete with selections on soft-deleted/archived notes**: DB cascades reach every row regardless of filters; the MultiSelect empty-row cleanup explicitly uses `IgnoreQueryFilters()` (§7.6). Archived notes need no special handling anywhere (no archive global filter — values visible to default queries).
- **E8 — Wrong-typed or malformed value payloads**: exactly-one-member rule + per-type checks (§7.2) → 400 with a field-specific message. `NaN`/`Infinity` numbers rejected (`double.IsFinite`). Text > 500 chars rejected (also model-validated via `[MaxLength]`).
- **E9 — Duplicate names**: definition names globally unique, option names unique per definition, both case-insensitive, both service-enforced (label precedent) → 409. Renames check the same rule excluding self.
- **E10 — Concurrency between property save and content auto-save**: none by construction — property endpoints never touch `NoteItem`, so they can neither trigger nor suffer a `DbUpdateConcurrencyException` against the content token (§7.1). Two tabs writing the same value: last write wins (single-user).
- **E11 — Checkbox normalization**: writing `false` deletes the row (INV-P1); filter `eq:false` therefore also matches never-set notes — documented UI copy "unchecked (or not set)" in the filter menu.
- **E12 — Filter/sort referencing a deleted definition**: 400 (unknown definition). The Home URL may hold stale filter state after a definition delete; the client drops unknown-definition filters on load instead of failing the page.
- **E13 — `pf` cap exceeded / oversized values**: > 20 filters → 400; filter value strings are validated per type before query building — no raw string ever reaches SQL (all values are EF parameters; no injection surface).
- **E14 — Account wipe**: wipe transaction deletes definitions after notes (§7.7); T-16 verifies no property rows of any kind survive.
- **E15 — Share page**: `SharedNote` is built by explicit projection (title + sanitized HTML) and never includes values; no leak path exists without a deliberate DTO change (O7).
- **E16 — Sorting when every note is empty for the property**: presence-key ordering degrades to the `UpdatedAt` desc tiebreak — stable, no error.

## 11. Test Scenarios

Service-level tests live in `Vanadium.Note.REST.Tests` (xUnit + EF Core SQLite in-memory), covering `PropertyService` and the `NoteService` filter/sort additions. **PostgreSQL-only behavior stays out of unit scope and is verified manually**: composite-FK cascade behavior differences, index usage (`EXPLAIN`), `ILike` interplay when `pf` combines with trigram search, and PostgreSQL NULL-ordering defaults (the presence-key ordering in §7.5 makes the suite provider-independent, but the manual pass confirms it on PostgreSQL). Note: `NumberValue` is `double` precisely so Number-sort tests run on SQLite (§4.1); `DateOnly` ordering works on both providers.

| # | Type | Scenario | Expected |
|---|---|---|---|
| T-1 | Normal | Create definitions of all six types; list with `includeUsage` | Ordered by SortOrder; counts zero |
| T-2 | Normal | Upsert each value kind on a note; `Get` note | `Properties` carries exactly the set values, correct typed members, ordered by definition SortOrder |
| T-3 | Normal | Overwrite a value (same definition) | Single row updated (INV-P2); other typed columns null (INV-P1) |
| T-4 | Normal | Clear value via DELETE; clear again | Row gone; second call still 204 (idempotent) |
| T-5 | Normal | Checkbox: set true, then set false | Row exists with `true`; then row deleted (E11) |
| T-6 | Normal | MultiSelect: set {A,B}, then {B,C}, then {} | Selection rows replaced exactly; empty list removes the value row |
| T-7 | Normal | Filters: each op of each type incl. combined date range and `anyof` | Matching note sets per §6.3/§7.3 semantics |
| T-8 | Normal | `Checkbox eq:false` matches unchecked AND never-set notes | Both returned |
| T-9 | Normal | Sort by Number asc/desc, Date, Text, Checkbox, Select (option order) | Ordered correctly; empty-valued notes always last; `UpdatedAt` desc tiebreak |
| T-10 | Boundary | `empty`/`notempty` for every type except Checkbox | NOT EXISTS / EXISTS semantics |
| T-11 | Boundary | Caps: 51st definition, 101st option, 501-char text, 21st filter | 400 each; boundary values (50/100/500/20) succeed |
| T-12 | Failure | Wrong-typed payloads (each cross-type combination), two members set, empty Text, non-finite Number, option from another definition | 400 each; nothing persisted |
| T-13 | Failure | Value write on archived note → 403; on soft-deleted note → 404; unknown definition → 404 | Per FR-4 |
| T-14 | Boundary | Type change with zero values; with an active-note value; with only a soft-deleted note's value (E6) | OK / 409 / 409 (proves `IgnoreQueryFilters`) |
| T-15 | Normal | Definition delete with values on active + archived + soft-deleted notes; restore the deleted note | All value rows gone; restore brings the note back with the property absent, no orphan rows |
| T-16 | Normal | Account wipe with definitions/options/values present | All four tables empty |
| T-17 | Normal | Option delete: Select values (incl. on a soft-deleted note) and MultiSelect selections; one MultiSelect value loses its last option (E7) | Select rows gone; selections gone; empty MultiSelect row cleaned up everywhere |
| T-18 | Failure | Duplicate definition name (case-insensitive), duplicate option name, rename collisions | 409 each |
| T-19 | Failure | `sortBy=prop:{unknown}` and `sortBy=prop:{multiSelectId}`; malformed `pf` triplets; op/type mismatch | 400 each |
| T-20 | Normal | Soft delete a note with values → its values invisible to filters; restore → filters match again | INV-P4 round trip |
| T-21 | Normal | `pf` + `search` + `labelIds` combined in `GetPaged` (both branches) and `GetAllSummaries` | Conjunction of all constraints; search branch ignores property sort |
| T-22 | Manual/UI | Panel controls per type incl. inline option create; read-only panel on archived note; filter bar chips + URL state; sort chip on rows; `/properties` page flows incl. usage-count confirmations; Swagger pass over the full status-code matrix | Per §9 |
| T-23 | Manual/PG | `EXPLAIN` on a filtered+sorted Home query against the dev DB confirms index usage (no seq scan on `NotePropertyValues`); cascade delete of a definition with ~1k values | NFR-10 |

Manual verification per repo workflow: `dotnet build Vanadium.slnx` clean → `dotnet ef database update` on the dev DB → Swagger over the new controller → both apps for the UI pass.

## 12. Implementation Order

1. **Schema**: entities + `NoteDbContext` configuration + `AddNoteProperties` migration + `AccountService` wipe line. Deploy-safe (nothing reads the tables yet).
2. **Definitions/options API**: `PropertyService` (CRUD, usage counts, lifecycle guards) + `PropertiesController` + tests T-1, T-11, T-14, T-16, T-18. Update `docs/api-specification.md`.
3. **Values API**: upsert/clear with validation + tests T-2…T-6, T-12, T-13, T-15, T-17, T-20. `NoteService.Get`/`GetPaged`/`GetAllSummaries` projections (`Properties` in responses).
4. **Filter/sort**: `pf` parsing in controllers + `ApplyPropertyFilters` + property sort + tests T-7…T-10, T-19, T-21.
5. **Frontend read/write**: Web DTOs + `PropertyService` client + `NotePropertyPanel` + `PropertyValueEditor` + editor wiring (read-only mode) + `/properties` page + NavMenu.
6. **Frontend list UX**: `PropertyFilterBar` on Home/Board + sort menu + row chip + URL state.
7. **Docs & wrap-up**: `CLAUDE.md` section (properties exist; value-table storage decision; INV-P4 `IgnoreQueryFilters` rule for definition-level scans; server-owned values), manual PG pass (T-22, T-23).

## 13. Open Questions

- **O1 — Label absorption timing**: recommendation is a follow-up release (§5). Trigger criteria to decide it: property UX stable, filter bar in daily use. Absorb then, or keep labels indefinitely for their lighter one-click ergonomics?
- **O2 — Type-conversion matrix**: v1 blocks type changes when values exist. Add best-effort conversion later (Number↔Text, Select→Text via option name, Text→Select by option matching)? Needs per-pair loss rules and a dry-run preview.
- **O3 — Text `contains` filter**: requires a trigram index on `TextValue` (NFR-3 forbids unindexed `ILIKE`). Worth an index on a ≤500-char column, or is `eq` enough in practice?
- **O4 — Exact decimal numbers**: `double` suffices for priorities/estimates but not money. A `Decimal` subtype (or per-definition precision) if a real need appears — blocked in tests by SQLite's no-decimal-ordering limitation, so it would also shift those tests to manual scope.
- **O5 — Date with time / reminders**: v1 is day-granular `DateOnly` (no timezone questions). Add optional time-of-day (→ `timestamptz`, UTC storage, local display) and/or end-date ranges later?
- **O6 — MultiSelect `allof` operator**: v1 has `eq`/`anyof`. `allof` is one more `EXISTS` per option — add when a concrete need shows up.
- **O7 — Properties on shared notes**: `/share/{token}` shows none in v1. If exposed later: read-only, rendered server-side into `SharedNote`, and only for explicitly whitelisted definitions (avoid leaking workflow metadata like "Client" fields).
- **O8 — `UpdatedAt` on property change**: v1 does not bump (§7.1). If "recently touched" sorting should reflect property edits, add a separate `MetadataUpdatedAt` (never the concurrency token) rather than bumping `UpdatedAt`.
- **O9 — Text sort collation**: PostgreSQL sorts by the DB collation; SQLite tests sort ordinal. Fine for ASCII-ish tags; revisit if localized text properties matter.
- **O10 — Richer list/board display**: per-definition "show on cards/rows" flag (chips for chosen properties on Home rows and Board cards), and Board **grouping** by a Select property (columns = options) — the natural v2 once values exist. Grouping likely wants drag-to-set-value, which is a larger interaction change.
- **O11 — Full-text search over property values**: include `Text`/`Select` values in search? Options: fold into `ContentText` derivation (couples value writes to note rows and reorders `UpdatedAt` semantics — disliked) vs. extending `ApplyFilters` with an indexed OR over `NotePropertyValues.TextValue` (needs the O3 trigram index). Deferred with search unchanged in v1.
- **O12 — Extended types roadmap**: `URL` (Text + link rendering — cheapest), `Status` (Select + grouping semantics), `Relation` (note↔note, needs its own join table and backlink story alongside the existing mention-based backlinks). Order of appetite: URL → Status → Relation.
