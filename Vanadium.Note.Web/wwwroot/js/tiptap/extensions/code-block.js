import { mergeAttributes } from '@tiptap/core'
import { createLowlight, common } from 'lowlight'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'

// ── Code block with lowlight syntax highlighting ─────────────────────────────

export const lowlight = createLowlight(common)

export const CodeBlock = CodeBlockLowlight.extend({
    renderHTML({ node, HTMLAttributes }) {
        const lang = node.attrs.language;
        return [
            'pre',
            mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
                'data-language': lang && lang !== 'plaintext' ? lang : null,
            }),
            ['code', { class: lang ? `language-${lang}` : null }, 0],
        ];
    },
});
