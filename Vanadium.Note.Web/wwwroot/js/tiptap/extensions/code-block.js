import { mergeAttributes } from '/js/vendor/tiptap-core.js'
import { createLowlight, common } from '/js/vendor/lowlight.js'
import CodeBlockLowlight from '/js/vendor/tiptap-extension-code-block-lowlight.js'

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
