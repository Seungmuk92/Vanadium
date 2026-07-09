import { Node } from 'https://esm.sh/@tiptap/core@2'

// ── NoteMention node ────────────────────────────────────────────────────────

export const NoteMention = Node.create({
    name: 'noteMention',
    group: 'inline',
    inline: true,
    atom: true,

    addAttributes() {
        return {
            noteId: { default: null },
            title:  { default: '' },
        };
    },

    parseHTML() {
        return [{
            tag: 'a[data-type="note-mention"]',
            getAttrs: el => ({
                noteId: el.getAttribute('data-note-id'),
                title:  el.getAttribute('data-title') || '',
            }),
        }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['a', {
            'data-type':    'note-mention',
            'data-note-id': HTMLAttributes.noteId,
            'data-title':   HTMLAttributes.title,
            class: 'note-mention',
        }, `@${HTMLAttributes.title}`];
    },
});
