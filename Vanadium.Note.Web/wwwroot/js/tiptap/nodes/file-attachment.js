import { Node, mergeAttributes } from 'https://esm.sh/@tiptap/core@2'

// ── FileAttachment node ──────────────────────────────────────────────────────

export const FileAttachment = Node.create({
    name: 'fileAttachment',
    group: 'inline',
    inline: true,
    atom: true,

    addAttributes() {
        return {
            href: { default: null },
            filename: { default: '' },
        };
    },

    parseHTML() {
        return [{
            tag: 'a.file-attachment',
            getAttrs: (el) => ({
                href: el.getAttribute('href'),
                filename: el.getAttribute('data-filename') || el.textContent.replace('📎 ', '').trim(),
            }),
        }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['a', mergeAttributes({
            class: 'file-attachment',
            'data-filename': HTMLAttributes.filename,
            download: HTMLAttributes.filename,
        }, { href: HTMLAttributes.href }), `📎 ${HTMLAttributes.filename}`];
    },
});
