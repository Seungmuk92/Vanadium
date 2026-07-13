import { Extension } from 'https://esm.sh/@tiptap/core@2'
import { Selection, TextSelection } from 'https://esm.sh/prosemirror-state@1.4.4'
import { findAncestorOfType, toggleOpenAt } from '../nodes/toggle.js'
import { collectFoldedHeadingSpans } from '../nodes/collapsible-heading.js'

// ── Toggle keymap ────────────────────────────────────────────────────────────

// True when the toggle's body is exactly one empty paragraph.
function isEmptyToggleBody(toggleNode) {
    const body = toggleNode.child(1);
    return body.childCount === 1
        && body.firstChild.type.name === 'paragraph'
        && body.firstChild.content.size === 0;
}

export const ToggleKeymap = Extension.create({
    name: 'toggleKeymap',
    priority: 1000, // must win over StarterKit's default Enter/Backspace

    addKeyboardShortcuts() {
        return {
            // Flip the nearest ancestor toggle from anywhere inside it, or the
            // collapsible heading the cursor is on.
            'Mod-Enter': ({ editor }) => {
                const { $from } = editor.state.selection;
                const toggle = findAncestorOfType($from, 'toggle');
                if (toggle) {
                    toggleOpenAt(editor.view, toggle.pos);
                    return true;
                }
                if ($from.parent.type.name === 'heading' && $from.parent.attrs.collapsible) {
                    toggleOpenAt(editor.view, $from.before($from.depth));
                    return true;
                }
                return false;
            },

            'Enter': ({ editor }) => {
                const { state, view } = editor;
                const { $from, empty } = state.selection;
                if (!empty) return false;

                // On a FOLDED collapsible heading, Enter expands the section
                // instead of splitting into the hidden range below it.
                if ($from.parent.type.name === 'heading'
                    && $from.parent.attrs.collapsible
                    && !$from.parent.attrs.open) {
                    const tr = state.tr.setNodeMarkup(
                        $from.before($from.depth), null, { ...$from.parent.attrs, open: true });
                    tr.setMeta('addToHistory', false);
                    view.dispatch(tr.scrollIntoView());
                    return true;
                }

                // Caret stranded inside a folded heading's hidden range
                // (selection guard could not find a visible escape, or older
                // content states): Enter expands the owning section(s)
                // instead of splitting invisible content.
                {
                    const spans = collectFoldedHeadingSpans(state)
                        .filter(s => $from.pos > s.from && $from.pos < s.to);
                    if (spans.length) {
                        const tr = state.tr;
                        for (const headingPos of new Set(spans.map(s => s.headingPos))) {
                            const h = state.doc.nodeAt(headingPos);
                            tr.setNodeMarkup(headingPos, null, { ...h.attrs, open: true });
                        }
                        tr.setMeta('addToHistory', false);
                        view.dispatch(tr.scrollIntoView());
                        return true;
                    }
                }

                if ($from.parent.type.name === 'toggleSummary') {
                    const toggle = findAncestorOfType($from, 'toggle');
                    if (!toggle) return false;
                    const group = findAncestorOfType($from, 'accordionGroup');
                    const inGroup = group && group.depth === toggle.depth - 1;

                    // (c) Summary of the LAST toggle of an accordion whose body
                    // is empty: append a new closed toggle to the group (FR-A4).
                    if (inGroup) {
                        const toggleEnd = toggle.pos + toggle.node.nodeSize;
                        const groupContentEnd = group.pos + group.node.nodeSize - 1;
                        if (toggleEnd === groupContentEnd && isEmptyToggleBody(toggle.node)) {
                            const newToggle = state.schema.nodes.toggle.createAndFill({ open: false });
                            if (!newToggle) return false;
                            const tr = state.tr.insert(groupContentEnd, newToggle);
                            tr.setSelection(TextSelection.create(tr.doc, groupContentEnd + 2));
                            view.dispatch(tr.scrollIntoView());
                            return true;
                        }
                    }

                    // (a) Move the cursor to the start of the body, opening the
                    // toggle if it is closed.
                    const tr = state.tr;
                    if (!toggle.node.attrs.open) {
                        tr.setNodeMarkup(toggle.pos, null, { ...toggle.node.attrs, open: true });
                        tr.setMeta('addToHistory', false);
                    }
                    const bodyPos = toggle.pos + 1 + toggle.node.child(0).nodeSize;
                    tr.setSelection(Selection.near(tr.doc.resolve(bodyPos + 1), 1));
                    view.dispatch(tr.scrollIntoView());
                    return true;
                }

                // Caret stranded inside the hidden body of a collapsed toggle
                // (e.g. it stayed in the body when the arrow was clicked):
                // Enter expands every closed ancestor toggle instead of typing
                // into invisible content.
                {
                    const tr = state.tr;
                    let opened = false;
                    for (let d = $from.depth; d > 0; d--) {
                        const n = $from.node(d);
                        if (n.type.name === 'toggle' && !n.attrs.open) {
                            tr.setNodeMarkup($from.before(d), null, { ...n.attrs, open: true });
                            opened = true;
                        }
                    }
                    if (opened) {
                        tr.setMeta('addToHistory', false);
                        view.dispatch(tr.scrollIntoView());
                        return true;
                    }
                }

                // (b) Trailing empty paragraph of a toggle body exits the toggle:
                // the empty paragraph is lifted below the toggle (below the whole
                // group when the toggle is an accordion item).
                if ($from.parent.type.name === 'paragraph' && $from.parent.content.size === 0) {
                    const bodyDepth = $from.depth - 1;
                    if (bodyDepth < 1 || $from.node(bodyDepth).type.name !== 'toggleContent') return false;
                    const body = $from.node(bodyDepth);
                    const toggle = findAncestorOfType($from, 'toggle');
                    if (!toggle) return false;
                    const isLast = $from.after($from.depth) === $from.end(bodyDepth);
                    if (!isLast) return false;

                    const group = findAncestorOfType($from, 'accordionGroup');
                    const container = group && group.depth === toggle.depth - 1 ? group : toggle;

                    const tr = state.tr;
                    if (body.childCount > 1) {
                        tr.delete($from.before($from.depth), $from.after($from.depth));
                    }
                    const afterPos = tr.mapping.map(container.pos + container.node.nodeSize);
                    tr.insert(afterPos, state.schema.nodes.paragraph.create());
                    tr.setSelection(TextSelection.create(tr.doc, afterPos + 1));
                    view.dispatch(tr.scrollIntoView());
                    return true;
                }

                return false;
            },

            // At the start of an empty summary: unwrap the toggle — its body
            // blocks are lifted to the toggle's level, the summary is dropped.
            // Accordion items lift outside the group (toggle+ forbids plain
            // blocks inside); a single-item group dissolves entirely.
            'Backspace': ({ editor }) => {
                const { state, view } = editor;
                const { $from, empty } = state.selection;
                if (!empty) return false;
                if ($from.parent.type.name !== 'toggleSummary') return false;
                if ($from.parent.content.size !== 0 || $from.parentOffset !== 0) return false;
                const toggle = findAncestorOfType($from, 'toggle');
                if (!toggle) return false;

                const bodyChildren = toggle.node.child(1).content;
                const group = findAncestorOfType($from, 'accordionGroup');
                const inGroup = group && group.depth === toggle.depth - 1;

                const tr = state.tr;
                if (inGroup && group.node.childCount === 1) {
                    // Last item: dissolve the group too (FR-A5).
                    tr.replaceWith(group.pos, group.pos + group.node.nodeSize, bodyChildren);
                    tr.setSelection(Selection.near(tr.doc.resolve(group.pos), 1));
                } else if (inGroup) {
                    const isFirst = group.pos + 1 === toggle.pos;
                    tr.delete(toggle.pos, toggle.pos + toggle.node.nodeSize);
                    const insertAt = isFirst ? group.pos : tr.mapping.map(group.pos + group.node.nodeSize);
                    tr.insert(insertAt, bodyChildren);
                    tr.setSelection(Selection.near(tr.doc.resolve(insertAt), 1));
                } else {
                    tr.replaceWith(toggle.pos, toggle.pos + toggle.node.nodeSize, bodyChildren);
                    tr.setSelection(Selection.near(tr.doc.resolve(toggle.pos), 1));
                }
                view.dispatch(tr.scrollIntoView());
                return true;
            },
        };
    },
});
