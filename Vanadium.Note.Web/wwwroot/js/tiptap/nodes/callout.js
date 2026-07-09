import { Node } from 'https://esm.sh/@tiptap/core@2'

// ── Callout node ────────────────────────────────────────────────────────────

export const Callout = Node.create({
    name: 'callout',
    group: 'block',
    content: 'block+',
    defining: true,

    addAttributes() {
        return {
            emoji: { default: '💡' },
        };
    },

    parseHTML() {
        return [{
            tag: 'div[data-type="callout"]',
            getAttrs: el => ({ emoji: el.getAttribute('data-emoji') || '💡' }),
        }];
    },

    renderHTML({ HTMLAttributes }) {
        return [
            'div',
            { 'data-type': 'callout', 'data-emoji': HTMLAttributes.emoji, class: 'callout-block' },
            ['span', { class: 'callout-emoji', contenteditable: 'false' }, HTMLAttributes.emoji],
            ['div', { class: 'callout-content' }, 0],
        ];
    },
});
