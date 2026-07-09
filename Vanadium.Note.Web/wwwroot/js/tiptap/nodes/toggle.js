import { Node, mergeAttributes } from 'https://esm.sh/@tiptap/core@2'

// ── Toggle nodes ─────────────────────────────────────────────────────────────
//
// Collapsible content blocks (Notion-style toggles). Folding is CSS-only:
// the body stays in the document and in the serialized HTML, so search
// (ContentText) and orphan-file scanning are unaffected. All user-visible
// text lives in element text content, never in attributes (hard rule).
// Design doc: docs/plannings/note-toggle-feature.md

// Flip the `open` attribute of the node at pos. Fold state changes are
// excluded from undo history (NFR-4) and work in read-only mode too:
// `editable: false` only blocks user input, not programmatic transactions.
export function toggleOpenAt(view, pos) {
    const node = view.state.doc.nodeAt(pos);
    if (!node) return;
    const tr = view.state.tr.setNodeMarkup(pos, null, { ...node.attrs, open: !node.attrs.open });
    tr.setMeta('addToHistory', false);
    view.dispatch(tr);
}

// Walk up from $pos and return the nearest ancestor of the given type.
export function findAncestorOfType($pos, typeName) {
    for (let d = $pos.depth; d > 0; d--) {
        const node = $pos.node(d);
        if (node.type.name === typeName) {
            return { node, pos: $pos.before(d), depth: d };
        }
    }
    return null;
}

// Parent container. The content expression enforces exactly one summary
// followed by one body — invalid shapes are unrepresentable.
export const Toggle = Node.create({
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
            // Best-effort import of external/native HTML pastes.
            { tag: 'details', getAttrs: el => ({ open: el.hasAttribute('open') }) },
        ];
    },

    renderHTML({ HTMLAttributes }) {
        return ['div', mergeAttributes(HTMLAttributes, {
            'data-type': 'toggle',
            class: 'toggle-block',
        }), 0];
    },

    addNodeView() {
        return ({ node, view, getPos }) => {
            const dom = document.createElement('div');
            dom.className = 'toggle-block';
            dom.setAttribute('data-type', 'toggle');
            dom.setAttribute('data-open', node.attrs.open ? 'true' : 'false');

            const arrow = document.createElement('button');
            arrow.type = 'button';
            arrow.className = 'toggle-arrow';
            arrow.contentEditable = 'false';
            arrow.title = 'Toggle (Ctrl+Enter)';
            arrow.setAttribute('aria-expanded', String(node.attrs.open));
            arrow.textContent = '▸';

            const inner = document.createElement('div');
            inner.className = 'toggle-inner';

            dom.appendChild(arrow);
            dom.appendChild(inner);

            // Prevent the editor from stealing focus / moving the caret.
            arrow.addEventListener('mousedown', e => e.preventDefault());
            arrow.addEventListener('click', e => {
                e.preventDefault();
                const pos = typeof getPos === 'function' ? getPos() : null;
                if (pos == null) return;
                toggleOpenAt(view, pos);
            });

            return {
                dom,
                contentDOM: inner,

                update(updatedNode) {
                    if (updatedNode.type.name !== 'toggle') return false;
                    dom.setAttribute('data-open', updatedNode.attrs.open ? 'true' : 'false');
                    arrow.setAttribute('aria-expanded', String(updatedNode.attrs.open));
                    return true;
                },

                stopEvent(event) {
                    return event.target === arrow || arrow.contains(event.target);
                },

                ignoreMutation(mutation) {
                    if (mutation.type === 'selection') return false;
                    return mutation.target === arrow || arrow.contains(mutation.target);
                },
            };
        };
    },
});

// Summary line: inline content only, exactly one line.
export const ToggleSummary = Node.create({
    name: 'toggleSummary',
    content: 'inline*',
    defining: true,
    selectable: false,

    parseHTML() {
        return [
            { tag: 'div[data-type="toggle-summary"]' },
            { tag: 'summary' }, // external <details> import
        ];
    },

    renderHTML() {
        return ['div', { 'data-type': 'toggle-summary', class: 'toggle-summary' }, 0];
    },
});

// Body: one or more blocks of any kind (incl. nested toggles/accordions).
export const ToggleContent = Node.create({
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
