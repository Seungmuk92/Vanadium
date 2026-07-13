import { mergeAttributes } from 'https://esm.sh/@tiptap/core@2'
import { createLowlight, common } from 'https://esm.sh/lowlight@3.3.0'
import CodeBlockLowlight from 'https://esm.sh/@tiptap/extension-code-block-lowlight@2'

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
