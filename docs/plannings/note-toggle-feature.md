# Feature Specification: Note Editor Collapsible Content (Toggle / Toggle Heading / Accordion)

Status: Draft for review
Author: smoh (written with Claude)
Date: 2026-06-12
Companion docs: `docs/plannings/note-archive-feature.md` (read-only editor mode), `CLAUDE.md` (editor interop rules)

## 1. Overview

The editor currently renders every block fully expanded. Technical notes accumulate bulky content — code blocks, logs, reference dumps — and long documents become hard to scan. This feature adds three collapsible constructs, in Notion's spirit:

1. **Toggle Block** — a one-line summary plus a collapsible body. The core feature.
2. **Toggle Heading** — an H1–H3 heading that folds the section below it (all sibling blocks up to the next heading of the same or higher level).
3. **Accordion Group** — a container of toggle blocks where at most one toggle is open at a time (mutually exclusive expansion).

All three are implemented purely in the editor layer (`tiptap-editor.js` + CSS). **No backend change is required**: content is still persisted as Tiptap HTML in `NoteItem.Content`, collapsed state is encoded in `data-*` attributes, and all user-visible text remains element text (never attribute values), so `ContentText` extraction, trigram search, and `OrphanFileCleanupJob` scanning keep working unmodified.

### Goals

- Let users fold away bulky content while keeping it in the document, searchable, and lossless.
- Persist the collapsed/expanded state as part of the note (the state a note is saved in is the state it reopens in).
- Keep folding usable in read-only mode (archived notes): fold/unfold works, content edits remain blocked.
- Follow the established custom-node pattern (`Callout`, `MermaidNode`, `PageLink`) — no new dependencies, no Tiptap Pro.

### Non-goals

- Lazy-loading or virtualizing collapsed content (everything stays in the DOM, just hidden via CSS).
- Per-user collapsed state (state is stored in the document itself, shared by anyone viewing the note — this is a single-user app).
- A dedicated markdown syntax for toggles (markdown round-trip is best-effort via inline HTML, see §4.4).
- Folding in non-editor render surfaces (e.g., if note HTML is ever rendered outside Tiptap, toggles render expanded — acceptable).

### Baseline (current behavior)

| Concern | Today |
|---|---|
| Block-level custom nodes | `Callout` (`div[data-type="callout"]`, `content: 'block+'`), `MermaidNode` (atom + NodeView), `PageLink` (atom) — all in `tiptap-editor.js` |
| Insert UX | Slash commands via `createSlashCommandsExtension` (`SLASH_COMMANDS` array + `command()` switch) |
| Headings | StarterKit default `Heading` node; H1–H3 exposed via slash commands and bubble menu |
| Persistence | `editor.getHTML()` → debounced 1500 ms auto-save → `NoteItem.Content`; server derives `ContentText = StripHtml(Content)` (regex `<[^>]+>` → space, so attribute values are **discarded**) |
| Search | GIN trigram index over `(Title, ContentText)`; `EF.Functions.ILike` per term |
| File GC | `OrphanFileCleanupJob` scans note HTML for `/uploads/file_{guid}` substrings |
| Read-only | Archived notes: `tiptapInterop.init(..., editable=false)` / `setEditable(false)`; auto-save never armed; server 403s `PUT` |
| Markdown | `tiptap-markdown` (`html: true`, `transformPastedText: true`); `getMarkdown()` used for export |

## 2. Requirements

### 2.1 Functional requirements — Toggle Block

- **FR-T1 Structure**: A toggle block consists of exactly one summary line (inline content) and one body (one or more block nodes). The body can contain any block content the editor supports: paragraphs, lists, code blocks, tables, images, callouts, mermaid diagrams, file attachments, page links, and other toggles.
- **FR-T2 Insert**: Insertable via slash command `/toggle`. A newly inserted toggle is **open**, has an empty summary with placeholder text "Toggle summary", and an empty paragraph as body; the cursor lands in the summary.
- **FR-T3 Fold/unfold**: Clicking the arrow control toggles the body's visibility. The arrow rotates (▸ closed / ▾ open). Collapsed state is stored on the node (`open` attribute) and serialized to HTML (`data-open`), so it survives save/reload.
- **FR-T4 Keyboard**:
  - `Enter` in the summary moves the cursor to the start of the body (opening the toggle if closed).
  - `Backspace` at the start of an empty summary unwraps the toggle (body blocks are lifted to the toggle's level; the empty summary is dropped).
  - `Mod-Enter` with the cursor anywhere inside a toggle flips its open state.
  - `Enter` on a trailing empty paragraph of the body exits the toggle (the empty paragraph is lifted below the toggle).
- **FR-T5 Folding hides, never deletes**: Collapsing only applies `display: none` styling to the body. The body's nodes remain in the ProseMirror document and in the serialized HTML.
- **FR-T6 Read-only**: With `editable = false`, fold/unfold still works; summary and body text cannot be edited. Fold state changes in read-only mode are in-memory only (nothing is saved; the server would reject the write anyway).
- **FR-T7 Nesting**: Toggles may be nested inside toggle bodies (and inside callouts, blockquotes, etc.). No artificial depth limit; see §6 E2 for practical guidance.

### 2.2 Functional requirements — Toggle Heading

- **FR-H1 Opt-in per heading**: Any H1–H3 heading can be made collapsible and back. A collapsible heading shows a fold arrow on hover (always visible when folded); a normal heading is unchanged. The document model stays a flat sequence — making a heading collapsible does **not** rewrap following blocks into a container node.
- **FR-H2 Fold range**: Folding a collapsible heading hides all following **sibling** blocks (same parent node) up to, but not including, the next heading of the same or higher level (lower `level` number), or the end of the parent.
- **FR-H3 Toggle via UI**: Slash commands `/toggleh1` `/toggleh2` `/toggleh3` create a collapsible heading (or convert the current block). An existing heading is converted via the same slash commands or by clicking the arrow gutter affordance. Converting a collapsible heading back to a plain heading (e.g., re-applying `setHeading` without the attribute or via a context action) un-hides any folded content.
- **FR-H4 Persistence**: `collapsible` and `open` are node attributes serialized as `data-collapsible` / `data-open` on the `<h1>`–`<h3>` tags.
- **FR-H5 Editing safety**: While a section is folded, typing occurs only in visible content. Operations that would move the cursor into a hidden range (ArrowDown/End at fold boundary) skip past the hidden range. Deleting the heading itself un-hides its folded section.
- **FR-H6 Read-only**: Same as FR-T6 — folding works in read-only mode.

### 2.3 Functional requirements — Accordion Group

- **FR-A1 Structure**: An accordion group is a block container holding one or more toggle blocks (and nothing else).
- **FR-A2 Mutual exclusion**: Within a group, opening a toggle closes any other open toggle in the same group. At most one toggle is open; zero open is allowed.
- **FR-A3 Insert**: Slash command `/accordion` inserts a group with two empty toggles (first one open).
- **FR-A4 Item management**: `Enter` at the end of the last toggle's summary while the body is empty appends a new toggle to the group (Notion-like). Deleting the last remaining toggle dissolves the group.
- **FR-A5 Dissolve**: A "dissolve" action (and `Backspace` at the very start of the first toggle's summary when the group has one child) unwraps the group, leaving its toggles as independent siblings — content lossless, exclusivity no longer enforced.
- **FR-A6 Nesting rules**: Accordion groups may not directly contain accordion groups. A toggle *body* inside an accordion may contain another accordion (exclusivity scopes are independent per group).
- **FR-A7 Read-only**: Fold/unfold with exclusivity still works in read-only mode (in-memory only).

### 2.4 Non-functional requirements

- **NFR-1 No new dependencies**: implemented with already-imported modules (`@tiptap/core` `Node`/`Extension`/`mergeAttributes`, `prosemirror-state` `Plugin`/`PluginKey`). The Tiptap Pro `Details` extension is explicitly **not** used. If decorations require `prosemirror-view` (`Decoration`, `DecorationSet`) and `prosemirror-model` (`Slice`/`Fragment`) imports, these are sub-dependencies of the existing esm.sh imports, not new top-level dependencies — same pattern as the existing `prosemirror-state` import.
- **NFR-2 Searchability**: all user-visible text (summaries, body text) must be element text content, never attribute values — `StripHtml` (regex `<[^>]+>` → space) must extract it into `ContentText`. This is a hard rule for every serialization decision in §4.
- **NFR-3 File GC safety**: `/uploads/file_{guid}` and `/api/images/{id}` references inside toggle bodies remain verbatim in the serialized HTML (guaranteed by FR-T5: folding is CSS-only).
- **NFR-4 Auto-save semantics**: fold/unfold in **editable** mode is a document change → triggers the existing 1500 ms debounced auto-save (decision rationale in §8 D1). Fold transactions carry `addToHistory: false` so Ctrl+Z never replays fold state changes.
- **NFR-5 Dark mode**: all new CSS in `wwwroot/css/app.css` must include `body.dark-mode` variants, following the existing callout/mermaid style pattern.
- **NFR-6 Interop discipline**: all new code lives in `tiptap-editor.js`; any new interop entry points are added to `window.tiptapInterop` and invoked only from `NoteEditor.razor`. (This feature is expected to need **no new interop functions** — see §5.)
- **NFR-7 English only**: all code, comments, UI strings (placeholders, slash command labels) in English.
- **NFR-8 No schema/API change**: no EF migration, no new endpoints, no DTO changes.

## 3. User Scenarios / Use Cases

| # | Actor goal | Flow | Outcome |
|---|---|---|---|
| UC-1 | Hide a bulky log dump | Type `/toggle` → Enter → type summary "Deployment log 2026-06-11" → Enter → paste log into body → click arrow to collapse | One-line summary in the document; log preserved, hidden, searchable |
| UC-2 | Keyboard-only authoring | `/toggle` → summary text → `Enter` (cursor moves into body) → write content → `Mod-Enter` to collapse → trailing-empty-paragraph `Enter` to exit below the toggle | No mouse needed end-to-end |
| UC-3 | Nest environment-specific notes | Create toggle "Prod setup", inside its body create toggle "Secrets handling" | Two-level nesting renders with indented arrows; both fold independently |
| UC-4 | Fold a finished section | Put the cursor on the "Research" H2 → `/toggleh2` → click the fold arrow | Everything until the next H2/H1 disappears from view; document outline stays scannable |
| UC-5 | FAQ-style reference note | `/accordion` → fill toggle 1 "How to rotate JWT secret", toggle 2 "How to reset dev DB" → open toggle 2 | Toggle 1 closes automatically; exactly one answer visible at a time |
| UC-6 | Copy a toggle elsewhere | Select a whole toggle block → Ctrl+C → paste into another position / another note | Toggle pastes intact (structure + open state); pasting into the same editor or another Vanadium note works because both share the same schema |
| UC-7 | Read an archived note | Open archived note (read-only banner) → click toggle arrows and heading folds | Folding works; text edits and slash commands remain blocked; nothing is saved |
| UC-8 | Find text hidden in a collapsed toggle | Home search for a term that only exists inside a collapsed toggle body | Note appears in results (trigram search over `ContentText` is fold-agnostic) |
| UC-9 | Export note as markdown | Editor menu → Download markdown | Toggles/accordions are emitted as inline HTML blocks inside the markdown (§4.4); headings export as `#`/`##`/`###` and silently lose fold attributes |
| UC-10 | Paste external `<details>` HTML | Paste HTML containing `<details><summary>…</summary>…</details>` | Best-effort conversion into a toggle block (§6 E10) |

## 4. Input / Output Specification

### 4.1 Toggle Block

Three new nodes, modeled after Tiptap Pro's Details trio but custom-built (`Node.create()`), following the `Callout` precedent.

Node schemas:

```js
// Parent container. content expression enforces exactly one summary then one body.
const Toggle = Node.create({
    name: 'toggle',
    group: 'block',
    content: 'toggleSummary toggleContent',
    defining: true,
    isolating: true,
    addAttributes() {
        return {
            open: {
                default: true,
                parseHTML: el => el.getAttribute('data-open') !== 'false',
                renderHTML: attrs => ({ 'data-open': attrs.open ? 'true' : 'false' }),
            },
        };
    },
    parseHTML() {
        return [
            { tag: 'div[data-type="toggle"]' },
            // Best-effort import of external/native HTML (see §6 E10):
            { tag: 'details', getAttrs: el => ({ open: el.hasAttribute('open') }) },
        ];
    },
    renderHTML({ HTMLAttributes }) {
        return ['div', mergeAttributes(HTMLAttributes, {
            'data-type': 'toggle',
            class: 'toggle-block',
        }), 0];
    },
    // NodeView adds the (non-editable) arrow button; see §5.3
});

// Summary line: inline content only, exactly one line.
const ToggleSummary = Node.create({
    name: 'toggleSummary',
    content: 'inline*',
    defining: true,
    selectable: false,
    parseHTML() {
        return [
            { tag: 'div[data-type="toggle-summary"]' },
            { tag: 'summary' },  // external <details> import
        ];
    },
    renderHTML() {
        return ['div', { 'data-type': 'toggle-summary', class: 'toggle-summary' }, 0];
    },
});

// Body: one or more blocks of any kind (incl. nested toggles).
const ToggleContent = Node.create({
    name: 'toggleContent',
    content: 'block+',
    defining: true,
    selectable: false,
    parseHTML() {
        return [{ tag: 'div[data-type="toggle-content"]' }];
    },
    renderHTML() {
        return ['div', { 'data-type': 'toggle-content', class: 'toggle-content' }, 0];
    },
});
```

Serialized HTML (what gets stored in `NoteItem.Content`):

```html
<div data-type="toggle" data-open="false" class="toggle-block">
  <div data-type="toggle-summary" class="toggle-summary">Deployment log 2026-06-11</div>
  <div data-type="toggle-content" class="toggle-content">
    <p>Rollout went fine except for the cache warmup:</p>
    <pre data-language="text"><code class="language-text">12:01:14 WARN cache ...</code></pre>
    <p><a class="file-attachment" data-filename="full-log.txt" href="https://.../uploads/file_3f2a...">📎 full-log.txt</a></p>
  </div>
</div>
```

Why `div[data-type]` instead of native `<details>/<summary>`:

- Native `<details>` toggles itself on summary click, fighting ProseMirror's view layer and selection model inside `contenteditable` (double-toggle, cursor loss). All visibility is therefore CSS-driven off `data-open`.
- Matches the existing `Callout`/`PageLink` serialization convention (`div[data-type=...]`).
- `parseHTML` still *accepts* `details`/`summary` so external HTML pastes degrade gracefully.

`StripHtml` check: the example above yields `Deployment log 2026-06-11 Rollout went fine ... 12:01:14 WARN cache ... 📎 full-log.txt` — summary and hidden body fully searchable (NFR-2 ✓). The `/uploads/file_{guid}` href survives verbatim (NFR-3 ✓).

### 4.2 Toggle Heading

**Not a new node** — the StarterKit `Heading` node is extended with two attributes (decision rationale in §8 D2). The document stays flat; folding range is computed, not structural.

```js
import Heading from 'https://esm.sh/@tiptap/extension-heading@2'

const CollapsibleHeading = Heading.extend({
    addAttributes() {
        return {
            ...this.parent?.(),
            collapsible: {
                default: false,
                parseHTML: el => el.getAttribute('data-collapsible') === 'true',
                renderHTML: attrs => attrs.collapsible ? { 'data-collapsible': 'true' } : {},
            },
            open: {
                default: true,
                parseHTML: el => el.getAttribute('data-open') !== 'false',
                renderHTML: attrs => attrs.collapsible && !attrs.open ? { 'data-open': 'false' } : {},
            },
        };
    },
});
// Registered with StarterKit.configure({ heading: false, codeBlock: false })
// plus CollapsibleHeading.configure({ levels: [1, 2, 3, 4, 5, 6] }) — UI exposes 1–3 as today.
```

Serialized HTML:

```html
<h2 data-collapsible="true" data-open="false">Research notes</h2>
<p>…hidden…</p>
<h3>Sub-finding</h3>          <!-- hidden too: level 3 > 2 -->
<p>…hidden…</p>
<h2>Next section</h2>          <!-- fold ends BEFORE this heading -->
```

A plain heading serializes exactly as today (`<h2>…</h2>`) — zero churn for existing notes, and a collapsible-but-open heading adds only `data-collapsible="true"`.

Fold-range rule (normative): for a heading H at index *i* among the children of parent P, the folded range is children *i+1 … j−1*, where *j* is the index of the first subsequent child of P that is a heading with `level <= H.level`, or `P.childCount` if none. Nodes inside the range are hidden via node decorations (`class: 'heading-folded'`), never removed.

### 4.3 Accordion Group

```js
const AccordionGroup = Node.create({
    name: 'accordionGroup',
    group: 'block',
    content: 'toggle+',          // toggles only; at least one
    isolating: true,
    parseHTML() {
        return [{ tag: 'div[data-type="accordion-group"]' }];
    },
    renderHTML({ HTMLAttributes }) {
        return ['div', mergeAttributes(HTMLAttributes, {
            'data-type': 'accordion-group',
            class: 'accordion-group',
        }), 0];
    },
});
```

Serialized HTML:

```html
<div data-type="accordion-group" class="accordion-group">
  <div data-type="toggle" data-open="true" class="toggle-block">…</div>
  <div data-type="toggle" data-open="false" class="toggle-block">…</div>
  <div data-type="toggle" data-open="false" class="toggle-block">…</div>
</div>
```

Parent–child rules (enforced by content expressions, not runtime checks):

| Parent | Allowed children | Notes |
|---|---|---|
| `doc`, `toggleContent`, `callout`, `blockquote`, list item content | `toggle`, `accordionGroup`, … (anything in group `block`) | Toggles nest anywhere blocks go |
| `toggle` | exactly `toggleSummary toggleContent` | Invalid combinations are unrepresentable |
| `toggleSummary` | `inline*` | No blocks, no hard breaks needed |
| `toggleContent` | `block+` | Never empty — minimum one (possibly empty) paragraph |
| `accordionGroup` | `toggle+` | `content: 'toggle+'` makes non-toggle children unrepresentable; ProseMirror auto-wraps/rejects on paste |
| `accordionGroup` → `accordionGroup` | forbidden directly | Guaranteed because `accordionGroup` is not in `toggle+`; nested accordions are only reachable via a toggle body (FR-A6) |

Mutual-exclusion state is **not** stored as a group attribute — it is an invariant over children's `open` attributes, enforced by an `appendTransaction` plugin (§5.4). The serialized form may therefore contain at most one `data-open="true"` toggle per group; documents violating this (hand-edited HTML) are normalized on first open-state change, not on load.

### 4.4 Markdown interaction (`tiptap-markdown`)

- **Serialization (`getMarkdown()` / export)**: none of the three constructs has a markdown form. With the existing `Markdown.configure({ html: true })`, nodes without a markdown serializer are emitted as raw HTML blocks inside the markdown output. This is the accepted behavior for toggle/accordion. `CollapsibleHeading` extends the stock heading, whose markdown serializer emits `## text` — the `data-collapsible`/`data-open` attributes are **silently dropped on markdown export** (documented, accepted loss; see §6 E11).
- **Paste (`transformPastedText: true`)**: markdown has no toggle syntax, so pasted markdown never creates toggles. Pasted HTML is handled by `parseHTML` rules: internal copies round-trip exactly; external `<details>` HTML converts best-effort (§6 E10).
- No changes to the Markdown extension configuration are required.

## 5. Processing Flows & Interface Design (`tiptap-editor.js`)

All additions go into `tiptap-editor.js`, registered in the `extensions` array of `tiptapInterop.init`. No new `tiptapInterop` functions and no `NoteEditor.razor` changes are expected (read-only behavior keys off `editor.isEditable`, which `setEditable` already controls).

### 5.1 New file sections (following the existing `// ── Section ──` convention)

| Section | Contents |
|---|---|
| `// ── Toggle nodes ──` | `Toggle`, `ToggleSummary`, `ToggleContent` node definitions + Toggle NodeView |
| `// ── Collapsible heading ──` | `CollapsibleHeading` + `headingFoldPlugin` |
| `// ── Accordion group ──` | `AccordionGroup` + `accordionExclusivityPlugin` |
| `// ── Toggle keymap ──` | `ToggleKeymap` extension (FR-T4, FR-A4) |

Registration changes in `init`:

```js
extensions: [
    StarterKit.configure({ codeBlock: false, heading: false }),
    CollapsibleHeading.configure({ levels: [1, 2, 3, 4, 5, 6] }),
    // ... existing ...
    Toggle, ToggleSummary, ToggleContent,
    AccordionGroup,
    ToggleKeymap,
    // ...
],
```

### 5.2 Slash commands

New `SLASH_COMMANDS` entries (icons follow the existing ASCII/emoji style):

```js
{ id: 'toggle',    label: 'Toggle',           desc: 'Collapsible block',          icon: '▸',  keywords: ['toggle', 'collapse', 'fold', 'details'] },
{ id: 'toggleh1',  label: 'Toggle Heading 1', desc: 'Collapsible large heading',  icon: '▸H1', keywords: ['toggleheading', 'foldheading'] },
{ id: 'toggleh2',  label: 'Toggle Heading 2', desc: 'Collapsible medium heading', icon: '▸H2', keywords: ['toggleheading', 'foldheading'] },
{ id: 'toggleh3',  label: 'Toggle Heading 3', desc: 'Collapsible small heading',  icon: '▸H3', keywords: ['toggleheading', 'foldheading'] },
{ id: 'accordion', label: 'Accordion',        desc: 'Exclusive toggle group',     icon: '≡',  keywords: ['accordion', 'faq', 'exclusive'] },
```

`command()` switch additions:

```js
case 'toggle':
    editor.chain().focus().insertContent({
        type: 'toggle',
        attrs: { open: true },
        content: [
            { type: 'toggleSummary' },
            { type: 'toggleContent', content: [{ type: 'paragraph' }] },
        ],
    }).run();
    // then place selection at the summary start
    break;
case 'toggleh1': editor.chain().focus().setHeading({ level: 1 })
    .updateAttributes('heading', { collapsible: true, open: true }).run(); break;
case 'toggleh2': /* level: 2, same pattern */ break;
case 'toggleh3': /* level: 3, same pattern */ break;
case 'accordion':
    editor.chain().focus().insertContent({
        type: 'accordionGroup',
        content: [ /* two empty toggles, first open, second closed */ ],
    }).run();
    break;
```

### 5.3 Toggle NodeView (arrow + fold behavior)

Follows the `MermaidNode` NodeView pattern (plain DOM, `stopEvent`, no framework). Unlike Mermaid it is **not** an atom — it exposes `contentDOM` so ProseMirror manages the children.

```text
NodeView DOM:
  div.toggle-block[data-open]            ← dom
  ├── button.toggle-arrow (contenteditable=false, aria-expanded, title "Toggle (Ctrl+Enter)")
  └── div.toggle-inner                   ← contentDOM (PM renders summary + content here)

CSS does the hiding (app.css):
  .toggle-block[data-open="false"] > .toggle-inner > [data-type="toggle-content"] { display: none; }
  .toggle-block[data-open="false"] > .toggle-arrow { transform: none; }      /* ▸ */
  .toggle-block[data-open="true"]  > .toggle-arrow { transform: rotate(90deg); } /* ▾ */
  body.dark-mode .toggle-block { … }    /* hover/arrow colors */
```

Fold/unfold flow (also used by the heading arrow and `Mod-Enter`):

```text
onArrowClick(view, getPos):
  pos  = getPos()
  node = view.state.doc.nodeAt(pos)
  tr   = view.state.tr.setNodeMarkup(pos, null, { ...node.attrs, open: !node.attrs.open })
  tr.setMeta('addToHistory', false)     // NFR-4: folding is not undoable
  view.dispatch(tr)
  // Editable editor:  onUpdate fires → OnEditorContentChanged → debounced auto-save (D1).
  // Read-only editor: dispatch still works (editable=false only blocks *user input*,
  //                   not programmatic transactions); NoteEditor.razor never arms
  //                   auto-save in read-only mode, so the change stays in-memory (FR-T6).
```

NodeView contract details:

- `update(node)`: re-applies `data-open` on `dom` when attrs change; returns `false` on type mismatch.
- `stopEvent(e)`: returns `true` only for events targeting `button.toggle-arrow`.
- `ignoreMutation`: only for mutations on the arrow button.
- Empty-summary placeholder: extend the existing `Placeholder.configure` with a `placeholder: ({ node }) => node.type.name === 'toggleSummary' ? 'Toggle summary' : 'Write something...'` function (the extension supports per-node placeholders; CSS hook `.is-empty::before` already exists for paragraphs — add the toggleSummary variant).

### 5.4 Accordion exclusivity plugin

```text
accordionExclusivityPlugin — ProseMirror Plugin, appendTransaction:

appendTransaction(transactions, oldState, newState):
  if no transaction changed the doc → return null
  fixes = []
  newState.doc.descendants((node, pos) => {
    if node.type != accordionGroup → continue
    openChildren = positions of children with attrs.open == true
    if openChildren.length <= 1 → continue
    // Determine which toggle the user just opened: the one whose attrs.open
    // changed false→true in this transaction batch (map oldState positions).
    keep = most recently opened child (fallback: first open child)
    for each other open child c: fixes.push(setNodeMarkup(c.pos, null, {...attrs, open:false}))
  })
  if fixes.empty → return null
  tr = newState.tr; apply all fixes; tr.setMeta('addToHistory', false); return tr
```

Properties: normalizes *any* path that opens a toggle (arrow click, `Mod-Enter`, programmatic, hand-edited HTML loaded then first touched) — single enforcement point, no logic duplicated in the NodeView.

### 5.5 Heading fold plugin

```text
headingFoldPlugin — ProseMirror Plugin:

props.decorations(state):
  decos = []
  for each child H of any block parent where H is heading && H.attrs.collapsible:
    // widget decoration: fold arrow button before the heading text
    decos.push(Decoration.widget(posOf(H)+1, makeArrowButton(H), { side: -1 }))
    if !H.attrs.open:
      for each sibling S in fold range (rule in §4.2):
        decos.push(Decoration.node(posOf(S), endOf(S), { class: 'heading-folded' }))
  return DecorationSet.create(state.doc, decos)

props.handleClick(view, pos, event):
  if event.target is .heading-fold-arrow:
    flip `open` attr of the owning heading (same dispatch pattern as §5.3, addToHistory:false)
    return true
  return false

CSS: .heading-folded { display: none; }
     h1..h3[data-collapsible] arrow affordance, hover reveal, dark-mode variants.
```

Selection guard (FR-H5): a `appendTransaction` step in the same plugin checks whether the selection ended inside a folded range after a transaction; if so it moves it to the nearest visible position after the range (mirrors how the cursor skips hidden content). Deleting a folded heading: the decorations are recomputed from the new doc on every transaction, so the hidden range simply un-hides — no cleanup code needed (the fold is derived state, never stored on the hidden nodes).

### 5.6 Keymap extension

```text
ToggleKeymap (Extension.addKeyboardShortcuts):
  'Mod-Enter'  → if selection is inside a toggle: flip nearest ancestor toggle's open attr (§5.3 dispatch); else false
  'Enter'      → (a) in toggleSummary: set open=true if closed, move cursor to start of toggleContent; return true
                 (b) in trailing empty paragraph of a toggleContent: lift the paragraph out below the toggle; return true
                 (c) in summary of the LAST toggle of an accordionGroup whose body is empty:
                     append a new closed toggle to the group, cursor into its summary; return true
                 (d) else false (default behavior)
  'Backspace'  → at start of an empty toggleSummary: unwrap the toggle
                 (replace toggle with its toggleContent children; drop the summary);
                 if it was the only child of an accordionGroup, dissolve the group too; return true
                 else false
```

### 5.7 Backend interface changes

**None.** Explicitly verified against the constraints:

| Constraint | Why it holds |
|---|---|
| `ContentText` / trigram search | All text is element text (§4); `StripHtml`'s `<[^>]+>` → space regex extracts summary + folded body text unchanged. No new searchable *fields* are added, so the GIN index is untouched. |
| `OrphanFileCleanupJob` | Folding never rewrites or removes body HTML; `/uploads/file_{guid}` substrings persist verbatim. |
| Archive read-only | Enforced server-side already (403 on `PUT`); the editor never attempts a save in read-only mode. |
| Auto-save | Reuses the existing `OnEditorContentChanged` → debounce path; no signature changes. |

## 6. Exceptions & Edge Cases

- **E1 — Empty toggle**: `content: 'toggleSummary toggleContent'` + `toggleContent: 'block+'` means ProseMirror always materializes an empty paragraph in the body — a toggle can never be truly empty or malformed. Empty summary shows the placeholder; collapsing an empty toggle is allowed (Notion parity).
- **E2 — Deep nesting**: no hard limit. CSS indents each level by a fixed offset (e.g., 1.25 rem); beyond ~5 levels content narrows but remains functional. No guard code; document in CSS comments.
- **E3 — Heavy content inside a toggle (code blocks, tables, images, mermaid)**: all allowed via `block+`. Two interactions need care: (a) images hidden by `display:none` still load — acceptable (they're Blob URLs resolved by `resolveAuthenticatedImages`, already fetched); (b) a Mermaid NodeView inside a *collapsed* toggle renders into a `display:none` container — SVG measurement can be wrong when first expanded. Mitigation: Mermaid's `update()` path already re-renders on attr change; if stale sizing is observed, re-render visible mermaid blocks when their ancestor toggle opens (listen in the Toggle NodeView's fold handler). Flagged as a known follow-up, not a blocker.
- **E4 — Toggle heading ↔ plain heading conversion**: governed by attrs only. Plain → collapsible adds `data-collapsible` (no content moves). Collapsible+folded → plain must reset `open: true` in the same transaction so the hidden range reappears (decorations recompute automatically). Changing a *folded* heading's level (e.g., H2→H3 via bubble menu) recomputes the fold range on the next decoration pass — content can pop in/out of the fold; acceptable, consistent with the range rule.
- **E5 — Selection/copy across a fold boundary**: ProseMirror selections may span hidden nodes (Ctrl+A, shift-click). Copy/delete then includes hidden content — this is **correct** (folding is presentation, not protection). No special handling; document it in the test plan (T-12) so it is asserted, not accidental.
- **E6 — Accordion dissolve & degenerate groups**: dissolving (FR-A5) lifts toggles to the parent; their `open` attrs persist as-is (multiple may be open afterwards — fine, exclusivity ends with the group). A group that loses its last toggle via other edits cannot exist (`toggle+` makes it invalid; ProseMirror removes the empty group node automatically).
- **E7 — Pasting a toggle into a toggle summary**: `toggleSummary` is `inline*`; block paste into it is re-slotted by ProseMirror (toggle lands after the summary's parent toggle, or inside the body, per standard fitting). No custom code; covered by T-10.
- **E8 — Pasting toggles into an accordion**: `toggle+` accepts them as items; non-toggle blocks pasted "into" a group fit before/after the group instead. Exclusivity normalizes multiple-open states on the next open action (§5.4 note).
- **E9 — Loading pre-feature notes / hand-edited HTML**: documents without the new attributes are unaffected (`collapsible` defaults false, `open` defaults true). Unknown/missing `data-open` parses as open (`!== 'false'`), failing safe toward visibility.
- **E10 — External `<details>` paste**: the extra `parseHTML` rules map `details` → toggle and `summary` → toggleSummary. Children of `details` after the summary are arbitrary; ProseMirror fits them into `toggleContent` per the content expression — best-effort, lossy edge cases acceptable (e.g., nested odd markup may flatten). If fitting fails entirely, content degrades to plain blocks (never data loss).
- **E11 — Markdown export of collapsible headings**: exports as a plain markdown heading; fold attributes dropped (stock heading serializer). Accepted: markdown export is a convenience snapshot, not a round-trip format. Toggles/accordions export as inline HTML and would round-trip if re-pasted (html:true).
- **E12 — Read-only fold state drift**: in read-only mode fold dispatches mutate the in-memory doc but are never saved; closing the note discards them. If the same note is later opened editable, the persisted state from the last *editable* session applies. No mitigation needed.
- **E13 — Concurrent sessions**: a fold in session A auto-saves and bumps the concurrency token; session B's next save hits the existing 409 conflict flow. Identical to any content edit today; no new handling (consequence of D1, see §8).

## 7. Test Scenarios

Editor logic is JS without a test harness (known limitation: Web project has no tests), so scenarios are split: **(S)** server-side xUnit tests in `Vanadium.Note.REST.Tests` and **(M)** manual editor verification per the repo workflow (both apps running).

| # | Type | Kind | Scenario | Expected |
|---|---|---|---|---|
| T-1 | Normal | M | `/toggle` insert, type summary + body, collapse, wait > 1.5 s, reload note | Toggle reopens collapsed; summary/body intact |
| T-2 | Normal | M | Keyboard path: Enter from summary into body; `Mod-Enter` collapse/expand; trailing-empty-paragraph Enter exits | Per FR-T4 |
| T-3 | Normal | M | Nested toggle (2 levels), fold inner, fold outer, expand outer | Inner fold state preserved independently |
| T-4 | Normal | M | `/toggleh2` on a section, fold; verify blocks until next H2/H1 hide (incl. an H3 in between); unfold | Per fold-range rule §4.2 |
| T-5 | Normal | M | `/accordion`, fill 3 items, click-open each in turn | At most one open at any time; opening N closes the previous |
| T-6 | Boundary | M | Empty toggle collapse; Backspace at empty summary start | Placeholder shown; unwrap leaves body blocks in place |
| T-7 | Boundary | M | Collapsed toggle containing code block + table + image + mermaid; expand | All render correctly after expand (E3 mermaid sizing check) |
| T-8 | Boundary | M | Convert folded toggle heading back to plain heading | Hidden range reappears; `data-*` attrs gone from saved HTML |
| T-9 | Boundary | M | Dissolve accordion with one open + two closed items | Three sibling toggles; states kept; no exclusivity afterwards |
| T-10 | Boundary | M | Copy whole toggle, paste into another note; paste markdown text into a summary | Structure + open state survive; markdown paste does not create blocks inside summary |
| T-11 | Boundary | M | Paste external `<details open><summary>x</summary><p>y</p></details>` | Becomes an open toggle "x" with body "y" |
| T-12 | Boundary | M | Ctrl+A copy with folded sections; Ctrl+Z after folding | Hidden content is included in copy (E5); undo never replays fold flips (NFR-4) |
| T-13 | Normal | M | Archived (read-only) note with toggles, toggle headings, accordion | Fold/unfold + exclusivity work; no edits possible; no save requests issued (verify via network tab) |
| T-14 | Normal | M | Dark mode on `/editor` with all three constructs | Arrows, hover states, folded-heading affordance legible in both themes |
| T-15 | Normal | S | `StripHtml` over toggle/accordion/collapsible-heading HTML fixtures (incl. `data-open="false"`) | `ContentText` contains summary + body text; contains **no** attribute fragments |
| T-16 | Normal | S | Search (`ApplyFilters`) for a term only inside a collapsed toggle body | Note matches (SQLite `ILike` approximation per existing test conventions; trigram behavior verified manually on dev DB) |
| T-17 | Normal | S | Note whose only reference to `file_{guid}` sits inside a collapsed toggle; run orphan cleanup scan | File treated as referenced; not deleted |
| T-18 | Failure | M | Hand-craft HTML with two `data-open="true"` toggles in one accordion (dev DB edit), open note, open a third item | Loads without error showing both open; first open action normalizes to exactly one open |
| T-19 | Failure | M | Toggle interactions while offline / API down | Fold works locally; auto-save failure surfaces via the existing save-error handling; no editor crash |

Manual pass checklist additions (per `CLAUDE.md` verification): `dotnet build Vanadium.slnx` clean; both apps running; exercise `/editor`, `/archive` (read-only), search from Home.

## 8. Implementation Considerations & Open Questions

### Decisions recommended in this document

- **D1 — Persist collapsed state: YES (recommended).** `open` is a node attribute serialized as `data-open`; fold/unfold in editable mode dispatches a doc transaction and rides the normal 1500 ms auto-save. Rationale: (a) the state a user leaves a note in is authoring intent — a curated note with collapsed log dumps should reopen curated; (b) attribute-based state costs nothing in backend/search/GC terms (§5.7); (c) Notion behaves this way. Costs accepted: fold actions bump `UpdatedAt` and the concurrency token (E13), and a "view-only gesture" writes to the DB. Mitigations baked in: `addToHistory: false` keeps undo clean; read-only mode never saves. **Alternative considered**: ephemeral-only folding (NodeView-local state, doc untouched) — simpler, zero save churn, but every reload re-expands everything, which defeats the curation use case; rejected.
- **D2 — Toggle heading as heading extension, not a new node (recommended).** Extending `Heading` with `collapsible`/`open` attrs keeps: markdown serialization (`##`) intact, all existing heading UX (slash, bubble menu, input rules `##␣`) working unchanged, and zero content migration. A dedicated `toggleHeading` node would duplicate heading behavior, serialize as non-semantic HTML, break markdown export, and require converting existing headings. The cost of the extension approach — fold range is *derived*, needing the decoration plugin (§5.5) — is the right complexity to take on. **Alternative considered**: a structural section container (heading + children wrapped in a node, like Notion's true block tree) — cleanest fold semantics but a major document-model change that would rewrap every existing document; rejected for v1.
- **D3 — Accordion ships last and is cut-eligible (recommended).** It is the lowest-value/highest-interaction-cost item of the three: exclusivity, item management (FR-A4/A5), and paste-fitting edge cases (E8) are all accordion-specific. It depends entirely on the Toggle Block primitives and adds no new serialization concepts, so deferring costs nothing architecturally. **Recommendation: implement Milestones 1–2, then re-evaluate whether the accordion is still wanted before building Milestone 3.** If FAQ-style notes turn out to be served well enough by plain adjacent toggles, drop it.

### Open questions (need a decision before/while implementing)

- **O1 — Fold-all/expand-all command**: a document-level "collapse all toggles" action (editor menu or `Mod-Alt-.`) is cheap once nodes exist. In scope for M1 polish or later?
- **O2 — Input rule for toggles**: Notion uses `>` + space; here `>` is blockquote (StarterKit). Options: `>>` + space → toggle, or no input rule (slash command only). Leaning: no input rule in v1 (avoid surprising blockquote users); revisit on demand.
- **O3 — Bubble menu integration**: add a "▸ Toggle" button to the bubble menu to wrap the current selection in a toggle (selection becomes the body, empty summary)? Useful but new wrap-logic; proposed as M1-optional.
- **O4 — Read-only initial state source**: archived notes open with whatever `data-open` state was last saved. Should the archive view instead force everything collapsed (overview-first) or everything expanded (nothing hidden)? Leaning: respect saved state (consistency); no extra code.
- **O5 — `transformCopiedText` interaction**: copying a toggle to the OS clipboard as markdown is currently disabled (`transformCopiedText: false`), so HTML flavor is used and in-app round-trip is safe. If this flag is ever enabled, toggle copies degrade to raw-HTML-in-markdown text. No action now; note for whoever flips that flag.
- **O6 — Mermaid-in-collapsed-toggle rendering** (E3): accept possible first-expand sizing glitch in v1, or wire the re-render hook immediately? Leaning: accept in v1, fix if observed on real diagrams.

## 9. Implementation Order (Milestones)

Each milestone is independently shippable and ends with the repo's standard verification (`dotnet build Vanadium.slnx`, run both apps, exercise the editor; plus the (S) tests added in that milestone).

1. **M1 — Toggle Block (core)**
   `Toggle`/`ToggleSummary`/`ToggleContent` nodes + NodeView + CSS (incl. dark mode) → `ToggleKeymap` (FR-T4) → slash command `/toggle` → placeholder support → read-only behavior pass (archived note) → (S) tests T-15/T-16/T-17 (toggle fixtures) → manual T-1, T-2, T-3, T-6, T-7, T-10, T-12, T-13, T-14, T-19.
   Exit criterion: a collapsed toggle containing a file attachment survives save → reload → search → orphan-cleanup cycle.
2. **M2 — Toggle Heading**
   `CollapsibleHeading` (StarterKit `heading: false` switch) → `headingFoldPlugin` (widget arrow + node decorations + selection guard) → slash commands `/toggleh1–3` → CSS → (S) fixture rows for heading attrs in T-15 → manual T-4, T-8, plus regression of plain headings (input rules `#␣`, bubble menu, markdown export).
   Exit criterion: pre-feature notes with plain headings show zero behavioral or serialization diff.
3. **M3 — Accordion Group** *(gated by D3 re-evaluation)*
   `AccordionGroup` node + `accordionExclusivityPlugin` → slash command `/accordion` → item management keymap (FR-A4) + dissolve (FR-A5) → CSS → manual T-5, T-9, T-18.
   Exit criterion: exclusivity holds across arrow clicks, `Mod-Enter`, and hand-edited multi-open documents.
4. **M4 — Documentation & polish**
   Update `CLAUDE.md` (new nodes in the custom-node list, serialization rule "user text must never live in attributes", slash command additions) → resolve O1/O3 if taken → close out E3 follow-up if observed.
