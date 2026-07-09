import { Node } from 'https://esm.sh/@tiptap/core@2'

// ── PageLink node ────────────────────────────────────────────────────────────

export const PageLink = Node.create({
    name: 'pageLink',
    group: 'block',
    atom: true,

    addAttributes() {
        return {
            noteId: { default: null },
            title:  { default: 'Untitled' },
        };
    },

    parseHTML() {
        return [{ tag: 'div[data-type="page-link"]', getAttrs: el => ({
            noteId: el.getAttribute('data-note-id'),
            title:  el.getAttribute('data-title') || 'Untitled',
        }) }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['div', {
            'data-type':    'page-link',
            'data-note-id': HTMLAttributes.noteId,
            'data-title':   HTMLAttributes.title,
            class: 'page-link-block',
        },
            ['span', { class: 'page-link-icon' }, '📄'],
            ['span', { class: 'page-link-title' }, HTMLAttributes.title],
        ];
    },
});
