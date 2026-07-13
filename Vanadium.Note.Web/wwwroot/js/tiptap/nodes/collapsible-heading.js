import { PluginKey, Plugin, Selection } from 'https://esm.sh/prosemirror-state@1.4.4'
import { Decoration, DecorationSet } from 'https://esm.sh/prosemirror-view@1.42.1'
import Heading from 'https://esm.sh/@tiptap/extension-heading@2'
import { toggleOpenAt } from './toggle.js'

// ── Collapsible heading ──────────────────────────────────────────────────────
//
// NOT a new node: the StarterKit Heading is extended with `collapsible` /
// `open` attributes (design decision D2). The document stays a flat sequence;
// the folded range is derived per render, never stored on the hidden nodes.
// A plain heading serializes exactly as before — zero churn for existing notes.

// Fold-range rule (§4.2): for heading H at child index i of parent P, the
// folded range covers children i+1 .. j-1, where j is the first subsequent
// child that is a heading with level <= H.level (or P's end).
// Returns per-sibling {from, to} spans for node decorations.
export function headingFoldedSpans(doc, headingNode, headingPos) {
    const $pos = doc.resolve(headingPos);
    const parent = $pos.parent;
    const index = $pos.index($pos.depth);
    const spans = [];
    let childPos = headingPos + headingNode.nodeSize;
    for (let j = index + 1; j < parent.childCount; j++) {
        const child = parent.child(j);
        if (child.type.name === 'heading' && child.attrs.level <= headingNode.attrs.level) break;
        spans.push({ from: childPos, to: childPos + child.nodeSize });
        childPos += child.nodeSize;
    }
    return spans;
}

// All folded sibling spans of every closed collapsible heading in the doc,
// each tagged with its owning heading's position. Shared by the selection
// guard and the Enter keymap (caret-in-hidden-range handling).
export function collectFoldedHeadingSpans(state) {
    const result = [];
    state.doc.descendants((node, pos) => {
        if (node.type.name !== 'heading' || !node.attrs.collapsible || node.attrs.open) return;
        for (const span of headingFoldedSpans(state.doc, node, pos)) {
            result.push({ from: span.from, to: span.to, headingPos: pos });
        }
    });
    return result;
}

function makeHeadingFoldArrow(open) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'heading-fold-arrow';
    btn.contentEditable = 'false';
    btn.title = 'Fold section (Ctrl+Enter)';
    btn.setAttribute('aria-expanded', String(open));
    btn.textContent = '▸';
    return btn;
}

const headingFoldPlugin = new Plugin({
    key: new PluginKey('headingFold'),

    props: {
        decorations(state) {
            const decos = [];
            state.doc.descendants((node, pos) => {
                if (node.type.name !== 'heading' || !node.attrs.collapsible) return;
                decos.push(Decoration.widget(
                    pos + 1,
                    () => makeHeadingFoldArrow(node.attrs.open),
                    { side: -1, key: `hfold-${pos}-${node.attrs.open}` }
                ));
                if (node.attrs.open) return;
                for (const span of headingFoldedSpans(state.doc, node, pos)) {
                    decos.push(Decoration.node(span.from, span.to, { class: 'heading-folded' }));
                }
            });
            return DecorationSet.create(state.doc, decos);
        },

        // mousedown (not click) so the caret never jumps into the heading first.
        // handleDOMEvents also fires with editable=false, so folding works in
        // read-only mode (FR-H6).
        handleDOMEvents: {
            mousedown(view, event) {
                const arrowEl = event.target?.closest?.('.heading-fold-arrow');
                if (!arrowEl) return false;
                event.preventDefault();
                const headingDom = arrowEl.closest('h1,h2,h3,h4,h5,h6');
                if (!headingDom) return true;
                const headingPos = view.posAtDOM(headingDom, 0) - 1;
                const node = view.state.doc.nodeAt(headingPos);
                if (node?.type.name === 'heading' && node.attrs.collapsible) {
                    toggleOpenAt(view, headingPos);
                }
                return true;
            },
        },
    },

    // Selection guard (FR-H5): if a transaction leaves the cursor inside a
    // folded range, skip it past (or before, when moving backwards) the range.
    // Deleting a folded heading needs no cleanup: decorations are recomputed
    // from the new doc, so its hidden range simply un-hides.
    appendTransaction(transactions, oldState, newState) {
        if (!transactions.some(tr => tr.docChanged || tr.selectionSet)) return null;
        const sel = newState.selection;
        if (!sel.empty) return null;

        const spans = collectFoldedHeadingSpans(newState);
        const inSpan = p => spans.some(s => p > s.from && p < s.to);
        if (!inSpan(sel.from)) return null;
        const span = spans.find(s => sel.from > s.from && sel.from < s.to);

        // Only an explicit forward move (ArrowDown/End) escapes forward.
        // A fold click (selection position unchanged) or a backward move
        // parks the caret at the end of the heading itself — visible, and
        // exactly where the next Enter expands the section again.
        const movedForward = sel.from > oldState.selection.from;
        let fix = movedForward
            ? Selection.near(newState.doc.resolve(span.to), 1)
            : Selection.near(newState.doc.resolve(span.from), -1);
        if (inSpan(fix.from)) {
            // Nothing visible in that direction (e.g. fold reaches the end
            // of the document) — escape to the other side instead.
            fix = movedForward
                ? Selection.near(newState.doc.resolve(span.from), -1)
                : Selection.near(newState.doc.resolve(span.to), 1);
        }
        if (inSpan(fix.from)) return null; // give up rather than loop

        return newState.tr.setSelection(fix);
    },
});

export const CollapsibleHeading = Heading.extend({
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

    addProseMirrorPlugins() {
        return [...(this.parent?.() ?? []), headingFoldPlugin];
    },
});
