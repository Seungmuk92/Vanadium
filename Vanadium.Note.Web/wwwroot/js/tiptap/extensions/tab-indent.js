import { Extension } from 'https://esm.sh/@tiptap/core@2'

// ── Tab / Shift-Tab handling ─────────────────────────────────────────────────

export const TabIndent = Extension.create({
    name: 'tabIndent',
    addKeyboardShortcuts() {
        return {
            Tab: ({ editor }) => {
                // Let Table extension handle cell navigation
                if (editor.isActive('tableCell') || editor.isActive('tableHeader')) {
                    return false;
                }
                if (editor.isActive('listItem')) {
                    return editor.chain().focus().sinkListItem('listItem').run();
                }
                if (editor.isActive('taskItem')) {
                    return editor.chain().focus().sinkListItem('taskItem').run();
                }
                // Use tr.insertText to bypass Markdown parsing (works in code blocks too).
                // Always return true to prevent the browser's default Tab (focus navigation).
                editor.chain().command(({ tr, dispatch }) => {
                    tr.insertText('    ');
                    dispatch?.(tr);
                    return true;
                }).run();
                return true;
            },
            'Shift-Tab': ({ editor }) => {
                // Let Table extension handle cell navigation
                if (editor.isActive('tableCell') || editor.isActive('tableHeader')) {
                    return false;
                }
                if (editor.isActive('listItem')) {
                    return editor.chain().focus().liftListItem('listItem').run();
                }
                if (editor.isActive('taskItem')) {
                    return editor.chain().focus().liftListItem('taskItem').run();
                }
                return false;
            },
        };
    },
});
