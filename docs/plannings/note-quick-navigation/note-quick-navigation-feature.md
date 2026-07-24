# Feature Specification: Quick Navigation

Status: Draft for review
Author: smoh (written with Claude)
Date: 2026-06-24
Companion docs: `docs/plannings/note-archive-feature.md`, `docs/recycle-bin-feature.md`

## 1. Overview

As notes accumulate, jumping to a specific note requires scrolling the Home list (`/`) or round-tripping through the Home search box. There is no keyboard-first entry point that works from *any* screen (Board, editor, Archive, Recycle Bin). This feature adds a **Quick Navigation** command palette — a modal overlay opened by a global shortcut (`Ctrl/Cmd+K`) that lets the user type, see live results, move the selection with the keyboard, and jump straight into a note. It mirrors Notion's Quick Find and VS Code's `Ctrl+P`: **shortcut → type → select → navigate**.

The palette reuses the existing PostgreSQL trigram full-text search (`Title` + `ContentText`) and surfaces archived notes (with an "Archived" badge) while excluding Recycle Bin notes. When the input is empty it shows recently visited notes so the most common jump (back to something just edited) needs zero typing.

### Goals

- One global shortcut opens a note switcher from every page, no mouse required.
- Reuse the existing trigram search path — no new `ILIKE` on non-indexed columns, no second search implementation to maintain.
- Empty-input state shows recent notes; typing switches to live search.
- Full keyboard loop: type → `↑`/`↓` → `Enter` to open → `Esc` to close. Mouse is supported but never required.
- Debounced, capped-result queries so typing stays responsive and cheap.

### Non-goals

- **Command/action execution** (create note, archive, toggle theme, etc.). v1 is pure note *navigation*. The DTO and component are named so actions can be layered on later (§13, O1).
- Replacing the Home search box / table view. Home stays the full management surface (pagination, label filters, multi-select, sorting). Quick Nav is an ephemeral overlay that *shares the backend*, not the component (§6.1).
- Fuzzy/typo-tolerant ranking beyond what trigram + recency gives. Server-side relevance scoring is an open question (O2), not v1.
- Cross-entity search (labels, attachments, users). Notes only.
- Server-side storage of browsing history / analytics.

## 2. Current Behavior (Baseline)

| Concern | Today |
|---|---|
| Note search | `NoteService.GetPaged(userId, page, pageSize, search, sortBy, sortDir, labelIds)` applies `ApplyFilters()` → `EF.Functions.ILike(Title, %term%) OR ILike(ContentText, %term%)` per whitespace term, backed by the GIN trigram index (`gin_trgm_ops`, migration `20260426140544_SwitchToTrigramSearch`). Search results **include archived notes** (`NoteSummary.IsArchived` flags them); the non-search Home branch excludes archived + sub-notes. Recycle Bin notes are excluded everywhere by the `DeletedAt == null` global query filter. |
| Search entry points | Home page only: a `<input class="notes-search">` with a 300 ms client debounce (`Home.razor` `OnSearchInput`) → `NoteService.GetAllAsync(...)` → `GET /api/notes?search=`. No way to search from Board, editor, Archive, or Recycle Bin. |
| Lightweight title search | `SearchForMention(userId, query, limit=10)` powers `@`-mention autocomplete (`GET /api/notes/mention-search?q=`); title-only `ILIKE`, **excludes archived notes** — not reusable as-is for Quick Nav (which must include archived and show snippets). |
| Recent notes | **Not tracked.** No "recently visited" concept anywhere in REST or Web. |
| Global keyboard shortcuts | **Already exists** (the original feature brief's claim that there is "no global shortcut infra" is stale). `Vanadium.Note.Web/Services/KeyboardShortcutService.cs` + `wwwroot/js/keyboard-shortcuts.js` (`window.keyboardShortcuts`) register chords in `MainLayout.razor` — currently `ctrl+n` (new note) and `ctrl+/` (shortcut help). Quick Nav registers one more chord through this same service rather than introducing new infra. |
| Result navigation | `NavigationManager.NavigateTo($"/editor/{id}")` opens a note; archived notes open read-only (archive feature). |
| Result DTO | `NoteSummary { Id, Title, UpdatedAt, ParentNoteId, ParentTitle, ChildCount, IsArchived, Labels }`. No content snippet/preview field exists. |

Implication: the backend search is reusable, but it returns a heavy projection (labels, child counts, parent titles) and has **no content snippet**. Quick Nav adds a *lean* read path beside it rather than overloading `GetPaged`.

## 3. Requirements

### 3.1 Functional requirements

- **FR-1 Global open/close**: A global shortcut (`Ctrl+K` on Windows/Linux, `Cmd+K` on macOS) opens the Quick Navigation dialog from any authenticated page. `Esc` (or clicking the backdrop) closes it. Re-pressing the open chord while open closes it (toggle). State is purely client-side.
- **FR-2 Live search**: Typing in the palette input queries notes by title + content using the existing trigram search and refreshes results live, debounced (§3.2 NFR-2). Results are capped at the top **N = 20**.
- **FR-3 Empty-input recents**: When the input is empty (or whitespace), the palette shows the user's most recently *visited* notes (most recent first), capped at **8**. Selecting one navigates exactly like a search result.
- **FR-4 Archive inclusion**: Archived notes appear in search results, tagged with an "Archived" badge. Recycle Bin notes never appear (excluded by the `DeletedAt == null` global filter — no opt-out needed; §5.3).
- **FR-5 Result content**: Each search result row shows the note **title** and a short **content snippet** (preview of `ContentText`), plus the "Archived" badge when applicable. Match highlighting in the snippet/title is in scope (§6.5) but degrades gracefully if disabled.
- **FR-6 Keyboard navigation**: `↑`/`↓` move the highlighted row (wrapping at ends), `Enter` opens the highlighted note, `Esc` closes. The list scrolls to keep the highlighted row visible. Mouse hover sets the highlight; click opens.
- **FR-7 Navigate**: Opening a result calls `NavigateTo("/editor/{id}")`, closes the palette, and records the note in Recents (§6.6). Archived notes open in read-only editor mode (existing behavior).
- **FR-8 Scope to user**: All results are limited to the current user's notes (`userId` from JWT), identical to every other note read path.
- **FR-9 Recents lifecycle**: A note is pushed to Recents whenever it is opened in the editor (search hit, Recents click, Home/Board click, page links, mentions — any path that lands on `/editor/{id}`). Recents is de-duplicated (most-recent wins) and bounded (§5.1).

### 3.2 Non-functional requirements

- **NFR-1 No new dependencies**: MudBlazor components (`MudDialog`/`MudOverlay`), the existing `KeyboardShortcutService`, and existing JS interop suffice. No new NuGet/npm packages.
- **NFR-2 Debounce + cap**: Input is debounced **250 ms** (consistent with the editor's debounced-`CancellationTokenSource` pattern and Home's 300 ms search) before a request fires; in-flight requests are cancelled on the next keystroke. Server caps results at `limit` (default 20, hard max 50).
- **NFR-3 Trigram-only search**: The query path reuses `ApplyFilters`-style `EF.Functions.ILike` per term against the GIN-trigram-indexed `(Title, ContentText)`. No `ILIKE`/filtering against non-indexed columns.
- **NFR-4 DTO duplication**: New DTOs are defined in `Vanadium.Note.REST/Models` and mirrored in `Vanadium.Note.Web/Models` per convention; no shared project.
- **NFR-5 English only**: All code, comments, and UI strings in English.
- **NFR-6 Structured logging**: The quick-search endpoint logs via Serilog with structured templates (correlation ID / username auto-enriched by middleware). Avoid logging full query strings at `Information` level (log term count / result count instead) to keep note content out of logs.
- **NFR-7 Minimum query length**: Queries shorter than **2 characters** (after trim) do not hit the server; the palette shows a hint instead (§7). Trigram on 1-char patterns is near-useless and scans broadly.
- **NFR-8 Latency budget**: Target < 150 ms p50 server time for a capped quick-search on a typical personal corpus; the trigram index plus `Take(limit)` keeps this bounded. Not a hard SLO — a budget to validate manually.

## 4. Decision: Recents storage (client `localStorage`, no schema)

The single most consequential design choice. **Decision: store Recents client-side in `localStorage`; add no table, no migration, no endpoint.**

Each visited note is stored as a small record the palette can render *without any network call*:

```jsonc
// localStorage key: "vanadium.recents.v1" (per-browser)
[
  { "id": "GUID", "title": "Note title at visit time", "isArchived": false, "visitedAt": "2026-06-24T09:15:00Z" }
]
```

Why client-side:

1. **Zero schema/endpoint cost.** A server-tracked history needs a `NoteVisit` table (or a `LastVisitedAt` column), a write on every editor open, a migration, a DTO, an endpoint, and pruning logic — disproportionate for a convenience list. Recents is inherently a per-device UX affordance, not shared data.
2. **Zero-latency empty state.** The palette renders Recents from `localStorage` synchronously on open; no spinner, no round-trip. This is the most-used path (jump back to what you just edited).
3. **Privacy.** Browsing history stays on the device and never enters server logs/DB.
4. **Consistent with existing client-only state.** `TokenStore` already wraps `localStorage`; Recents follows the same JS-interop pattern.

Trade-offs (accepted, with mitigations):

- **No cross-device sync.** Recents differs per browser. Acceptable — it is a recency hint, not durable data. (Server-tracked Recents is O3 if cross-device becomes desired.)
- **Stale entries** (a note was renamed, archived, soft-deleted, or permanently deleted after its visit). Mitigation: the stored `title` is a *display cache*; navigation always resolves live against the server, and dead targets are handled at open time (§7, E3/E4). Recents entries that 404 on navigation are pruned lazily.
- **localStorage is title-stale.** A renamed note shows its old title in Recents until next visit. Acceptable; refreshed on next open. (Optional batch refresh is O4.)

Bounded size: keep the **most recent 25** entries in storage (more than the 8 shown, so pruning a few stale ones still leaves a full list). Eviction is most-recent-wins, oldest-out.

## 5. Data Model

### 5.1 No schema change

Quick Nav introduces **no entity or schema changes** and therefore **no EF Core migration**. Recents lives in `localStorage` (§4); search reuses existing indexed columns.

If a future iteration moves Recents server-side (O3), that *would* require a migration (`NoteVisit` table or `NoteItem.LastVisitedAt`) — explicitly out of scope here and called out so the v1 author does not add one.

### 5.2 Reused index

The existing GIN trigram index on `(Title, ContentText)` (`gin_trgm_ops`) serves the quick-search query unchanged. No new index.

### 5.3 Global query filter — use the default, no opt-out

Per the repo rule ("any new code that scans note content or bulk-deletes notes must consider `IgnoreQueryFilters()`"): the quick-search query is a **read** that must see **active + archived** notes and **exclude Recycle Bin** notes. That is exactly what the default `DeletedAt == null` global filter already provides. Therefore quick-search uses the **default-filtered** `db.Notes` set and **does not** call `IgnoreQueryFilters()`. Archived notes are included by *not* adding the optional `ArchivedAt == null` predicate that Home/Board/children/mention paths use. This is a deliberate, documented choice, mirroring how `GetPaged`'s search branch already includes archived notes.

## 6. Processing Flows

### 6.1 Component relationship (palette vs. Home search)

```text
                       ┌─────────────────────────────────────┐
   Ctrl/Cmd+K  ───────►│  QuickNavDialog.razor (overlay)      │
   (any page)          │  - input + debounce + key nav        │
                       │  - empty → Recents (localStorage)    │
                       │  - typing → GET /api/notes/quick-search
                       └───────────────┬─────────────────────┘
                                       │ shares backend (trigram)
                                       ▼
   Home search box ───► GET /api/notes?search=  (full NoteSummary, paged, labels)
   (unchanged in v1)         │
                             └── both ultimately hit ApplyFilters trigram path
```

The palette is a **separate, reusable component** hosted once in `MainLayout.razor`, not a reuse of the Home table. They share the *backend search semantics* (trigram `ILike`) but serve different jobs: Home is a paginated management view; Quick Nav is a transient navigator with a lean payload and snippets. Converting Home's search box into the same palette is deferred (O5).

### 6.2 Open / close (global shortcut)

```text
MainLayout.OnAfterRenderAsync (first render):
  ShortcutService.RegisterAsync("ctrl+k", "Quick navigation", ToggleQuickNav)
  // KeyboardShortcutService maps both Ctrl+K and Cmd+K to the same "ctrl+k" id (§8.3)

ToggleQuickNav():
  _quickNavOpen = !_quickNavOpen
  StateHasChanged()
  // when opening: child component resets input, loads Recents, focuses input

QuickNavDialog (on @bind-Open true):
  clear query, selectedIndex = 0
  results = ReadRecentsFromLocalStorage()   // synchronous-ish JS interop, no server call
  focus(inputElement)
```

### 6.3 Debounced live search

```text
OnInput(value):
  query = value.Trim()
  _searchCts?.Cancel(); _searchCts = new CTS(); token = _searchCts.Token
  selectedIndex = 0

  if query.Length == 0:
      results = ReadRecents(); mode = Recents; return
  if query.Length < 2:                       // NFR-7
      results = []; mode = NeedMoreChars; return

  try:
      await Task.Delay(250, token)           // NFR-2 debounce
      mode = Searching
      var r = await NoteService.QuickSearchAsync(query, limit: 20, token)
      if r.IsSuccess: results = r.Value; mode = (results.Count==0 ? NoResults : Results)
      else:           results = []; mode = Error
  catch OperationCanceledException: /* superseded by next keystroke — ignore */
```

### 6.4 Backend quick-search query

`NoteService.QuickSearch` (REST) — lean projection, archived included, trigram-backed:

```text
QuickSearch(userId, query, limit):
  terms = query.Trim().Split(' ', RemoveEmpty|TrimEntries)
  if terms.Length == 0: return []
  limit = Clamp(limit, 1, 50)

  q = db.Notes.Where(n => n.UserId == userId)          // global filter hides Recycle Bin
  // NOTE: no n.ArchivedAt == null predicate → archived notes INCLUDED (FR-4)
  foreach term in terms:
      pattern = "%" + EscapeLikePattern(term) + "%"     // reuse existing helper
      q = q.Where(n => EF.Functions.ILike(n.Title, pattern)
                    || EF.Functions.ILike(n.ContentText, pattern))   // GIN trigram

  rows = await q.OrderByDescending(n => n.UpdatedAt)     // ranking: recency (O2)
                .Take(limit)
                .Select(n => new {
                    n.Id, n.Title, n.ContentText, n.ParentNoteId, n.ArchivedAt
                }).ToListAsync()

  return rows.Select(r => new QuickNavResult {
      Id = r.Id,
      Title = r.Title,
      Snippet = BuildSnippet(r.ContentText, terms),       // §6.5, computed in memory
      IsArchived = r.ArchivedAt != null
  }).ToList()
```

`OrderByDescending(UpdatedAt)` is the v1 ranking — most-recently-edited matches first, cheap and predictable. Relevance ranking (term frequency / `similarity()` / title-match boost) is O2.

### 6.5 Snippet + highlight

`BuildSnippet(contentText, terms)` runs in memory on the capped result set (≤ limit rows), so it never touches the DB:

```text
BuildSnippet(content, terms):
  if content is null/empty: return ""
  idx = first case-insensitive index of any term in content
  start = max(0, idx - 30)
  slice = content.Substring(start, up to 160 chars)
  prefix = start > 0 ? "…" : ""
  suffix = (start+len) < content.Length ? "…" : ""
  return prefix + slice + suffix            // plain text; ContentText is already tag-stripped
```

`ContentText` is the server-derived, tag-stripped text used for trigram search (per `StripHtml`), so snippets are plain text with no markup-injection risk. **Highlighting** is applied client-side: the palette wraps occurrences of each term in the title/snippet with a `<mark>`-styled span at render time (terms are escaped before being used in a client-side regex). If highlighting is disabled, rows still render correctly (FR-5 graceful degradation).

### 6.6 Recording a visit (Recents write)

```text
On editor load of an existing note (NoteEditor after GET succeeds):
  await JS: quickNav.pushRecent({ id, title, isArchived, visitedAt: nowUtc })

quickNav.pushRecent (JS, localStorage):
  list = read("vanadium.recents.v1") or []
  list = list.filter(e => e.id !== entry.id)     // de-dupe
  list.unshift(entry)                            // most-recent first
  list = list.slice(0, 25)                       // bound (§5.1)
  write("vanadium.recents.v1", list)
```

The write happens in `NoteEditor.razor` after a successful `GET /api/notes/{id}` (so we only record notes that actually resolved, with their current title/archived state). This single hook covers every navigation source (FR-9), because all of them land on `/editor/{id}`.

## 7. Input / Output Specification (API & DTOs)

### 7.1 New endpoint

| Method & route | Behavior | Responses |
|---|---|---|
| `GET /api/notes/quick-search?q={query}&limit={n}` | Trigram search over `Title`+`ContentText` for the current user; **includes archived**, excludes Recycle Bin; lean projection with snippet; ordered by `UpdatedAt` desc; capped at `limit` (default 20, max 50). | `200 List<QuickNavResult>` (empty list if `q` < 2 chars or no matches) |

Why a dedicated endpoint instead of reusing `GET /api/notes?search=`:

- **Lean payload.** Quick Nav needs only `Id`, `Title`, `Snippet`, `IsArchived` — not labels, child counts, parent titles, or pagination metadata. Reusing `GetPaged` would ship a heavier `PagedResult<NoteSummary>` and still lack a snippet.
- **Snippet computation.** Adding a snippet to `NoteSummary` would bloat the Home/Board projection and pull `ContentText` into every list call. A separate result type keeps `ContentText` out of the common path.
- **Independent evolution.** Action items (O1) and relevance ranking (O2) can land on this endpoint without touching the Home list contract.

`q` is `[MaxLength(200)]`. `limit` clamped server-side to `[1, 50]`.

### 7.2 New DTO (REST + Web mirror)

`Vanadium.Note.REST/Models/QuickNavResult.cs`, mirrored in `Vanadium.Note.Web/Models/QuickNavResult.cs`:

```csharp
namespace Vanadium.Note.REST.Models;   // Web mirror: Vanadium.Note.Web.Models

/// <summary>Lean search hit for the Quick Navigation palette.
/// Intentionally smaller than NoteSummary: no labels, child counts, or parent title.</summary>
public class QuickNavResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Plain-text preview around the first match, tag-free (derived from ContentText). May be empty.</summary>
    public string Snippet { get; set; } = string.Empty;
    /// <summary>True if the note is archived — drives the "Archived" badge. Such notes open read-only.</summary>
    public bool IsArchived { get; set; }
}
```

### 7.3 No change to existing DTOs / endpoints

`NoteSummary`, `PagedResult<T>`, `GET /api/notes`, `mention-search`, `summaries`, `children` are **unchanged**. Quick Nav is purely additive on the backend.

### 7.4 Recents — client-side shape (no DTO/endpoint)

Recents has no server contract. The client record (§4) is managed entirely by `wwwroot/js/quick-nav.js` and surfaced to Blazor as a small model used only for rendering:

```csharp
// Vanadium.Note.Web/Models/RecentNote.cs  (Web-only; never sent to the server)
public class RecentNote
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTime VisitedAt { get; set; }
}
```

### 7.5 Web client method

`Vanadium.Note.Web/Services/NoteService.cs` (follows the existing `ServiceResult<T>` + `CancellationToken` pattern):

```csharp
public async Task<ServiceResult<List<QuickNavResult>>> QuickSearchAsync(
    string query,
    int limit = 20,
    CancellationToken cancellationToken = default)
{
    try
    {
        var url = $"api/notes/quick-search?q={Uri.EscapeDataString(query)}&limit={limit}";
        var result = await http.GetFromJsonAsync<List<QuickNavResult>>(url, cancellationToken);
        return ServiceResult<List<QuickNavResult>>.Ok(result ?? []);
    }
    catch (OperationCanceledException) { throw; }   // superseded by next keystroke
    catch (Exception ex)
    {
        logger.LogError(ex, "Quick search failed.");
        return ServiceResult<List<QuickNavResult>>.Fail("Search failed.");
    }
}
```

## 8. Interface Design

### 8.1 Backend changes by file

| File | Change |
|---|---|
| `Models/QuickNavResult.cs` | New lean DTO (§7.2) |
| `Services/NoteService.cs` | New `QuickSearch(Guid userId, string query, int limit)` + private `BuildSnippet(string?, string[])`; reuse existing `EscapeLikePattern` and the `ApplyFilters` term-split pattern. **No global filter opt-out** (§5.3) |
| `Controllers/NotesController.cs` | New `GET /api/notes/quick-search` action (below); structured log of term/result counts (NFR-6) |

REST signatures:

```csharp
// NoteService.cs
public async Task<List<QuickNavResult>> QuickSearch(Guid userId, string query, int limit = 20);
private static string BuildSnippet(string? contentText, string[] terms);

// NotesController.cs
[HttpGet("quick-search")]
public async Task<ActionResult<List<QuickNavResult>>> QuickSearch(
    [FromQuery][MaxLength(200)] string q = "",
    [FromQuery] int limit = 20)
{
    var userId = await GetUserId();
    var results = await noteService.QuickSearch(userId, q, limit);
    return Ok(results);
}
```

### 8.2 Frontend changes by file

| File | Change |
|---|---|
| `Models/QuickNavResult.cs` (Web) | New DTO mirror |
| `Models/RecentNote.cs` (Web) | New client-only model (§7.4) |
| `Services/NoteService.cs` (Web) | `QuickSearchAsync(...)` client method (§7.5) |
| `Services/QuickNavService.cs` (Web) | New thin service wrapping `quick-nav.js` interop: `Task PushRecentAsync(RecentNote)`, `Task<List<RecentNote>> GetRecentsAsync()`, `Task RemoveRecentAsync(Guid)`. Registered scoped in `Program.cs`. |
| `Components/QuickNavDialog.razor` | **New** palette component: input, debounced search, Recents empty-state, keyboard navigation, result rows with badge + snippet + highlight. Self-contained (own `@code` debounce CTS + key handling). |
| `Layout/MainLayout.razor` | Host `<QuickNavDialog @bind-Open="_quickNavOpen" />` once; register `ctrl+k` chord via `ShortcutService`; `ToggleQuickNav()` |
| `Pages/NoteEditor.razor` | After a successful note load (existing-note branch), call `QuickNavService.PushRecentAsync(...)` to record the visit (FR-9, §6.6) |
| `wwwroot/js/quick-nav.js` | New `window.quickNav` interop module: `getRecents()`, `pushRecent(entry)`, `removeRecent(id)` over `localStorage` key `vanadium.recents.v1` |
| `wwwroot/index.html` | `<script src="js/quick-nav.js"></script>` alongside `keyboard-shortcuts.js` |

`QuickNavDialog.razor` skeleton:

```razor
@inject NoteService NoteService
@inject QuickNavService QuickNav
@inject NavigationManager Nav

<MudOverlay Visible="Open" DarkBackground="true" @onclick="Close" ZIndex="9999">
  <div class="quicknav-panel" @onclick:stopPropagation="true" @onkeydown="OnKeyDown">
    <input @ref="_input" class="quicknav-input"
           placeholder="Search notes…" value="@_query" @oninput="OnInput" />
    @* mode: Recents | NeedMoreChars | Searching | Results | NoResults | Error *@
    <ul class="quicknav-results">
      @for (var i = 0; i < _rows.Count; i++)
      {
        var idx = i; var row = _rows[i];
        <li class="@(idx == _selected ? "is-selected" : "")"
            @onmouseenter="() => _selected = idx"
            @onclick="() => OpenRow(row)">
          <span class="title">@RenderHighlighted(row.Title)</span>
          @if (row.IsArchived) { <MudChip Size="Size.Small">Archived</MudChip> }
          <span class="snippet">@RenderHighlighted(row.Snippet)</span>
        </li>
      }
    </ul>
  </div>
</MudOverlay>

@code {
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    private ElementReference _input;
    private string _query = string.Empty;
    private int _selected;
    private List<QuickNavRow> _rows = [];      // unified shape over QuickNavResult / RecentNote
    private CancellationTokenSource? _searchCts;
    private QuickNavMode _mode = QuickNavMode.Recents;

    protected override async Task OnParametersSetAsync()
    {
        if (Open && !_wasOpen) await OnOpened();   // load recents + focus
        _wasOpen = Open;
    }

    private async Task OnInput(ChangeEventArgs e) { /* §6.3 */ }
    private void OnKeyDown(KeyboardEventArgs e)  { /* ArrowUp/Down/Enter/Esc — §8.4 */ }
    private async Task OpenRow(QuickNavRow row)
    {
        await Close();
        Nav.NavigateTo($"/editor/{row.Id}");       // archived → read-only editor
    }
    private async Task Close() { await OpenChanged.InvokeAsync(false); }
}
```

### 8.3 Global shortcut handling (`Ctrl/Cmd+K`)

The existing `KeyboardShortcutService` + `keyboard-shortcuts.js` already register chords app-wide and call back into Blazor. Quick Nav adds one registration in `MainLayout`:

```csharp
await ShortcutService.RegisterAsync("ctrl+k", "Quick navigation",
    () => InvokeAsync(ToggleQuickNav));
```

Two small extensions to the existing JS handler are required and should be verified against its current code:

1. **Map `Cmd+K` (macOS, `metaKey`) to the same `ctrl+k` chord** so the shortcut is `Ctrl+K` on Windows/Linux and `Cmd+K` on macOS. If `keyboard-shortcuts.js` currently checks only `ctrlKey`, extend the chord match to `(e.ctrlKey || e.metaKey)` for this chord.
2. **Fire even while a text input/textarea is focused.** Some global-shortcut handlers ignore keystrokes originating in inputs. `Ctrl/Cmd+K` must open the palette even when the editor or a search box has focus. Confirm the handler does not early-return for input targets for this chord (and that it `preventDefault()`s to avoid the browser's focus-address-bar binding on some platforms).

`Esc` and arrow/Enter handling live **inside** `QuickNavDialog` (scoped `@onkeydown`), not in the global service — they should only act while the palette is open and must not leak to the page.

### 8.4 Keyboard map (inside the palette)

| Key | Action |
|---|---|
| `↓` | `_selected = (_selected + 1) % _rows.Count` (wrap), scroll into view |
| `↑` | `_selected = (_selected - 1 + _rows.Count) % _rows.Count` (wrap), scroll into view |
| `Enter` | Open `_rows[_selected]` (no-op if list empty) |
| `Esc` | Close palette (`preventDefault`, `stopPropagation`) |
| `Ctrl/Cmd+K` | Handled globally → toggles closed |

## 9. User Scenarios / Use Cases

### 9.1 Jump back to a recent note (empty input)

User presses `Ctrl/Cmd+K` from the Board. The palette opens focused, showing the 8 most recently visited notes from `localStorage` (no spinner). `↓` `↓` `Enter` opens the third one in the editor. Total: four keystrokes, no mouse, no typing.

### 9.2 Find and open by content (live search)

From the editor, user presses `Cmd+K`, types `oauth token`. After 250 ms the palette shows up to 20 notes whose title or content matches both terms, each with a snippet and the matched words highlighted. `Enter` opens the top hit.

### 9.3 Open an archived note

User searches `retro 2025`; one result carries an "Archived" badge. Selecting it navigates to `/editor/{id}`, which opens **read-only** (existing archive behavior). Recents records it with `isArchived: true`, so it shows the badge in the empty-input list too.

### 9.4 No results / too short

Typing `q` (1 char) shows "Type at least 2 characters." Typing `zzzplugh` (no match) shows "No notes found." Both states are reachable and dismissible with `Esc`.

### 9.5 Recents points at a deleted note

A note visited yesterday was permanently deleted. It still appears in Recents (title cached locally). Selecting it navigates to `/editor/{id}`, the editor's `GET` returns 404, the editor redirects to Home with a snackbar "That note no longer exists," and the entry is pruned from Recents (§E3/E4).

### 9.6 Use-case summary

| # | Actor goal | Flow | Outcome |
|---|---|---|---|
| UC-1 | Return to something just edited | `Ctrl/Cmd+K` → arrows → `Enter` | Opens recent note, no typing |
| UC-2 | Find a note by remembered content | `Ctrl/Cmd+K` → type → `Enter` | Opens top trigram match |
| UC-3 | Reach an archived note fast | search → badge row → `Enter` | Read-only editor opens |
| UC-4 | Keyboard-only navigation from any page | open on Board/editor/Archive | Palette works identically everywhere |
| UC-5 | Recover gracefully from a stale recent | pick dead entry | Redirect to Home + prune, no crash |

## 10. Edge Cases

- **E1 — Very short / whitespace query**: `< 2` non-whitespace chars → no server call; palette shows "Type at least 2 characters" (NFR-7). Pure whitespace is treated as empty → Recents.
- **E2 — Rapid typing / superseded requests**: Each keystroke cancels the prior `CancellationTokenSource`; cancelled requests throw `OperationCanceledException` and are swallowed. Only the latest response renders — cancellation guarantees the in-flight request is abandoned before its result can overwrite a newer one.
- **E3 — Recents entry → soft-deleted note**: The note is in the Recycle Bin. It still shows in Recents (local cache). On open, `GET /api/notes/{id}` returns 404 (global filter hides soft-deleted) → editor redirects to Home with a snackbar; `QuickNavService.RemoveRecentAsync(id)` prunes it.
- **E4 — Recents entry → permanently deleted note**: Same as E3 (404 → redirect + prune). The prune keeps Recents self-healing without any server bookkeeping.
- **E5 — Search hit becomes invalid between render and open**: A note matched, but was deleted by another session before the user pressed `Enter`. Navigation 404s → handled exactly like E3 (redirect + snackbar). The palette never blocks on liveness checks.
- **E6 — Cross-user isolation**: `QuickSearch` filters `n.UserId == userId`; a guessed/forged id in a Recents entry (e.g., copied localStorage) still 404s on `GET` because the editor's note fetch is user-scoped. No cross-user data leaks via Recents or search.
- **E7 — Unauthenticated / expired JWT**: `quick-search` is behind the same auth as other note endpoints → 401 when the token is expired; the Web client surfaces a failed `ServiceResult` and the existing auth flow redirects to `/login`. The palette shows the error state, not stale results. Opening the palette itself requires no network, so it can open even while offline — search then shows the error state.
- **E8 — Empty corpus / new user**: No notes and no Recents → empty-input state shows "No recent notes yet — start typing to search." Searching returns `[]` → "No notes found."
- **E9 — Shortcut collision**: `Ctrl/Cmd+K` can clash with browser/OS bindings (e.g., Firefox focuses the search bar on `Ctrl+K`). The handler `preventDefault()`s the chord while the app has focus (§8.3). Document the chord in the `Ctrl+/` shortcut-help dialog. If a hard conflict is reported, the chord is centralized in `KeyboardShortcutService`, so changing it is a one-line edit (O6).
- **E10 — Palette open across navigation**: Selecting a result calls `Close()` *before* `NavigateTo`, so the overlay is dismissed and won't linger over the editor. If the user navigates via another means while open (browser back), `MainLayout` keeps `_quickNavOpen` false on route change (reset in `OnParametersSet`/location-changed handler) to avoid a stuck overlay.
- **E11 — Long titles / snippets**: Title and snippet are single-line, CSS-truncated with ellipsis; snippet is hard-capped at ~160 chars server-side (§6.5) so payloads stay small even for huge notes.
- **E12 — Highlight injection**: Terms are escaped before building the client-side highlight regex, and `ContentText`/`Title` are plain text (tag-stripped), so `<mark>` wrapping cannot inject markup. Render highlighted fragments as text nodes + styled spans, not via raw HTML, to be safe.
- **E13 — Snippet with no match in content** (match was in the *title* only): `BuildSnippet` finds no term in `ContentText` → returns the leading 160 chars (or empty). Row still renders with the title highlighted.
- **E14 — `limit` out of range**: Client always sends 20; a hand-crafted request with `limit=0` or `limit=9999` is clamped server-side to `[1, 50]` (§6.4).

## 11. Test Scenarios

Backend logic is covered in `Vanadium.Note.REST.Tests` (xUnit + EF Core SQLite/in-memory, per the existing test project). **Trigram `ILike` behavior is PostgreSQL-only and out of unit scope** — SQLite/in-memory does not implement `EF.Functions.ILike`/`gin_trgm_ops`, so term-matching relevance must be verified **manually** against the dev PostgreSQL DB (Swagger + the running apps). Unit tests target the parts that are provider-agnostic: result shape, archive inclusion, Recycle Bin exclusion, capping/clamping, snippet building, and user scoping. Recents is client-side and verified via the manual/UI pass.

| # | Type | Scenario | Expected |
|---|---|---|---|
| T-1 | Normal | `QuickSearch` with a query matching 3 notes (1 archived) | Returns all 3; archived one has `IsArchived = true`; Recycle Bin note never returned |
| T-2 | Normal | Result projection | Each `QuickNavResult` has `Id`, `Title`, `Snippet`, `IsArchived` only — no labels/child counts leaked |
| T-3 | Boundary | `limit = 0` and `limit = 9999` | Clamped to 1 and 50 respectively |
| T-4 | Boundary | More matches than `limit` | Exactly `limit` rows, ordered by `UpdatedAt` desc |
| T-5 | Boundary | `q = ""`, `q = " "`, `q = "a"` (after the controller is hit directly) | Empty list (no terms / below min handled; client also guards at 2 chars) |
| T-6 | Failure | Cross-user query (user B's notes) | Never returned; `UserId` scoping enforced |
| T-7 | Normal | Soft-deleted note that matches the query | Excluded (global filter) without any `IgnoreQueryFilters()` |
| T-8 | Unit | `BuildSnippet` with match mid-content | Returns ≤ ~160-char window with leading/trailing `…`; plain text |
| T-9 | Unit | `BuildSnippet` with title-only match / null content | Returns leading slice or empty string; no exception |
| T-10 | Manual/PG | Two-term query (`oauth token`) on dev PostgreSQL | Only notes matching **both** terms (trigram `ILIKE` AND-of-terms), index used (check `EXPLAIN`) |
| T-11 | Manual/UI | `Ctrl+K` (Win/Linux) and `Cmd+K` (macOS) from Home, Board, editor, Archive, Recycle Bin | Palette opens everywhere; input focused; `Esc` closes; re-press toggles |
| T-12 | Manual/UI | Empty input | Shows up to 8 Recents from `localStorage`, most-recent first, with archive badges |
| T-13 | Manual/UI | Keyboard nav | `↑/↓` wrap + scroll; `Enter` opens highlighted; mouse hover sets highlight; click opens |
| T-14 | Manual/UI | Open a result | Navigates to `/editor/{id}`, palette closes, note recorded in Recents |
| T-15 | Manual/UI | Recents → soft-deleted/permanently-deleted note | Navigation 404 → redirect to Home + snackbar; entry pruned (E3/E4) |
| T-16 | Manual/UI | Archived result | Badge shown; opens read-only editor |
| T-17 | Manual/UI | Debounce | Fast typing fires at most one request per 250 ms pause; no flicker of stale results (E2) |
| T-18 | Manual/UI | Highlight | Matched terms wrapped/styled in title + snippet; special-character terms don't break rendering (E12) |

Manual verification per repo workflow: `dotnet build Vanadium.slnx` clean (no new warnings); `dotnet run` REST + Swagger pass over `GET /api/notes/quick-search` (archived inclusion, Recycle Bin exclusion, clamping); both apps running for the full keyboard/UI pass on all five pages. No `dotnet ef database update` needed (no schema change).

## 12. Implementation Order

1. **Backend**: `QuickNavResult` DTO + `NoteService.QuickSearch` + `BuildSnippet` + controller endpoint + structured log. Verify via Swagger (archived in, Recycle Bin out, clamping). No migration.
2. **Web data layer**: `QuickNavResult` mirror, `RecentNote` model, `NoteService.QuickSearchAsync`, `quick-nav.js` + `QuickNavService`, `index.html` script include.
3. **Palette**: `QuickNavDialog.razor` (input, debounce, Recents, key nav, rows, badge, snippet, highlight).
4. **Wiring**: host in `MainLayout`, register `ctrl+k` (+ Cmd mapping & input-focus firing in `keyboard-shortcuts.js`), `ToggleQuickNav`, reset-on-route-change; `NoteEditor` Recents push; document chord in the `Ctrl+/` help dialog.
5. **Tests + docs**: provider-agnostic unit tests (T-1…T-9); update `CLAUDE.md` (new `quick-search` endpoint; Quick Nav reuses trigram search with archived included / Recycle Bin excluded via default filter; Recents is client-side `localStorage`, no schema).

## 13. Open Questions

- **O1 — Command/actions in the palette**: v1 is note-navigation only. Should the palette later host actions (new note, archive current, go to Board, toggle theme) à la a true command palette? The `QuickNavResult`/component naming leaves room; a `kind` discriminator on results would be the extension point.
- **O2 — Ranking**: v1 orders by `UpdatedAt` desc among trigram matches. Better relevance (title-match boost, `pg_trgm similarity()` score, term frequency) would need either an ordered `similarity()` query (still index-assisted) or client-side scoring on the capped set. Worth it for large corpora — measure first.
- **O3 — Server-side Recents**: Move Recents server-side (`NoteVisit` table or `LastVisitedAt`) for cross-device sync? Costs a migration + write-on-open + endpoint. Deferred unless multi-device use emerges; v1's `localStorage` is the deliberate lightweight choice (§4).
- **O4 — Recents title freshness**: Renamed/archived notes show stale title/badge in Recents until next visit. Add an optional batch "resolve recents" call (`POST /api/notes/resolve?ids=`) returning current title/state? Adds a (small) endpoint; weigh against the privacy/simplicity of zero-network Recents.
- **O5 — Unify Home search with the palette**: Should the Home search box open/become the same `QuickNavDialog`, or stay a distinct paginated table view? v1 keeps them separate (§6.1); converging them later removes a code path but changes Home's UX.
- **O6 — Shortcut choice & discoverability**: Is `Ctrl/Cmd+K` the right chord (vs. `Ctrl+P` VS Code-style, which collides with browser print)? Surface it in the existing `Ctrl+/` help overlay and as a hint in the Home search placeholder. Confirm no clash with planned future shortcuts.
- **O7 — Min query length & debounce tuning**: 2 chars / 250 ms are starting points aligned with the editor and Home. Validate against real latency on the dev DB; trigram on 2-char patterns can still scan broadly on large corpora — consider raising to 3 if p50 exceeds the NFR-8 budget.
