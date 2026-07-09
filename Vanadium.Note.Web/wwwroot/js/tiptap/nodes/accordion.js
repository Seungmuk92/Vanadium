import { Node, mergeAttributes } from 'https://esm.sh/@tiptap/core@2'
import { PluginKey, Plugin } from 'https://esm.sh/prosemirror-state'

// ── Accordion group ──────────────────────────────────────────────────────────
//
// A container of toggles where at most one is open at a time. Exclusivity is
// an invariant over the children's `open` attributes, enforced in a single
// place by appendTransaction — every path that opens a toggle (arrow click,
// Mod-Enter, programmatic, hand-edited HTML once touched) is normalized here.

const accordionExclusivityPlugin = new Plugin({
    key: new PluginKey('accordionExclusivity'),

    appendTransaction(transactions, oldState, newState) {
        if (!transactions.some(tr => tr.docChanged)) return null;

        // Positions (in new-state coordinates) of toggles that were already
        // open before this transaction batch — used to find the one the user
        // just opened, which is the one to keep.
        const previouslyOpen = new Set();
        oldState.doc.descendants((node, pos) => {
            if (node.type.name !== 'toggle' || !node.attrs.open) return;
            let mapped = pos;
            for (const t of transactions) mapped = t.mapping.map(mapped);
            previouslyOpen.add(mapped);
        });

        const fixes = [];
        newState.doc.descendants((node, pos) => {
            if (node.type.name !== 'accordionGroup') return;
            const openChildren = [];
            let childPos = pos + 1;
            node.forEach(child => {
                if (child.attrs.open) openChildren.push(childPos);
                childPos += child.nodeSize;
            });
            if (openChildren.length <= 1) return;
            const keep = openChildren.find(p => !previouslyOpen.has(p)) ?? openChildren[0];
            for (const p of openChildren) {
                if (p !== keep) fixes.push(p);
            }
        });

        if (fixes.length === 0) return null;
        const tr = newState.tr;
        for (const p of fixes) {
            const n = newState.doc.nodeAt(p);
            tr.setNodeMarkup(p, null, { ...n.attrs, open: false });
        }
        tr.setMeta('addToHistory', false);
        return tr;
    },
});

export const AccordionGroup = Node.create({
    name: 'accordionGroup',
    group: 'block',
    content: 'toggle+', // toggles only, at least one; nested groups unrepresentable (FR-A6)
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

    addProseMirrorPlugins() {
        return [accordionExclusivityPlugin];
    },
});
