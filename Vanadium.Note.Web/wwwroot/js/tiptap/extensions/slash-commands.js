import { Extension } from 'https://esm.sh/@tiptap/core@2'
import { PluginKey } from 'https://esm.sh/prosemirror-state@1.4.4'
import Suggestion from 'https://esm.sh/@tiptap/suggestion@2'

// ── Slash commands ───────────────────────────────────────────────────────────

const SLASH_COMMANDS = [
    { id: 'page',     label: 'New Page',      desc: 'Create a sub-note',    icon: '📄', keywords: ['page', 'subnote'] },
    { id: 'h1',       label: 'Heading 1',     desc: 'Large heading',        icon: 'H1', keywords: ['heading'] },
    { id: 'h2',       label: 'Heading 2',     desc: 'Medium heading',       icon: 'H2', keywords: ['heading'] },
    { id: 'h3',       label: 'Heading 3',     desc: 'Small heading',        icon: 'H3', keywords: ['heading'] },
    { id: 'bullet',   label: 'Bullet List',   desc: 'Unordered list',       icon: '•',  keywords: ['list', 'ul'] },
    { id: 'numbered', label: 'Numbered List', desc: 'Ordered list',         icon: '1.', keywords: ['list', 'ol'] },
    { id: 'table',    label: 'Table',         desc: 'Insert a table',       icon: '⊞',  keywords: ['table', 'grid'] },
    { id: 'quote',    label: 'Quote',         desc: 'Block quotation',      icon: '"',  keywords: ['blockquote'] },
    { id: 'todo',     label: 'Task List',     desc: 'Checkbox list',        icon: '☑',  keywords: ['todo', 'task', 'check', 'checkbox'] },
    { id: 'callout',  label: 'Callout',       desc: 'Highlighted callout',  icon: '💡', keywords: ['callout', 'note', 'tip', 'info', 'highlight'] },
    { id: 'code',     label: 'Code Block',    desc: 'Monospace code block', icon: '</>', keywords: ['codeblock'] },
    { id: 'mermaid',  label: 'Diagram',       desc: 'Mermaid diagram',      icon: '◈',  keywords: ['mermaid', 'diagram', 'flowchart', 'chart', 'graph'] },
    { id: 'divider',  label: 'Divider',       desc: 'Horizontal rule',      icon: '—',  keywords: ['hr', 'rule'] },
    { id: 'toggle',    label: 'Toggle',           desc: 'Collapsible block',          icon: '▸',   keywords: ['toggle', 'collapse', 'fold', 'details'] },
    { id: 'toggleh1',  label: 'Toggle Heading 1', desc: 'Collapsible large heading',  icon: '▸H1', keywords: ['toggleheading', 'foldheading'] },
    { id: 'toggleh2',  label: 'Toggle Heading 2', desc: 'Collapsible medium heading', icon: '▸H2', keywords: ['toggleheading', 'foldheading'] },
    { id: 'toggleh3',  label: 'Toggle Heading 3', desc: 'Collapsible small heading',  icon: '▸H3', keywords: ['toggleheading', 'foldheading'] },
    { id: 'accordion', label: 'Accordion',        desc: 'Exclusive toggle group',     icon: '≡',   keywords: ['accordion', 'faq', 'exclusive'] },
];

// After a block insert, place the caret inside the summary of the nearest
// matching node at/before the selection (i.e. the one just inserted).
function placeCursorInInsertedSummary(editor, typeName, summaryOffset) {
    const { state } = editor;
    let found = null;
    state.doc.descendants((node, pos) => {
        if (pos > state.selection.from) return false;
        if (node.type.name === typeName) found = pos;
    });
    if (found !== null) editor.commands.setTextSelection(found + summaryOffset);
}

export function createSlashCommandsExtension(dotnetRef) {
    return Extension.create({
        name: 'slashCommands',
        addProseMirrorPlugins() {
            return [
                Suggestion({
                    editor: this.editor,
                    pluginKey: new PluginKey('slashCommands'),
                    char: '/',
                    allowSpaces: false,
                    items({ query }) {
                        const q = query.toLowerCase();
                        return SLASH_COMMANDS.filter(cmd =>
                            !q ||
                            cmd.id.startsWith(q) ||
                            cmd.label.toLowerCase().startsWith(q) ||
                            cmd.keywords.some(k => k.startsWith(q))
                        );
                    },
                    render() {
                        let menu = null;
                        let selectedIndex = 0;
                        let currentItems = [];
                        let currentCommand = null;

                        const renderItems = () => {
                            if (!menu) return;
                            menu.innerHTML = '';
                            currentItems.forEach((item, i) => {
                                const row = document.createElement('div');
                                row.className = 'slash-menu-item' + (i === selectedIndex ? ' slash-menu-item-active' : '');
                                row.innerHTML = `<span class="slash-menu-icon">${item.icon}</span><div class="slash-menu-text"><span class="slash-menu-label">${item.label}</span><span class="slash-menu-desc">${item.desc}</span></div>`;
                                row.addEventListener('mousedown', e => {
                                    e.preventDefault();
                                    currentCommand?.(item);
                                });
                                menu.appendChild(row);
                            });
                            menu.querySelector('.slash-menu-item-active')
                                ?.scrollIntoView({ block: 'nearest' });
                        };

                        const reposition = clientRect => {
                            if (!menu || !clientRect) return;
                            const rect = clientRect();
                            if (!rect) return;
                            const menuWidth = 260;
                            const spaceBelow = window.innerHeight - rect.bottom;
                            const menuHeight = Math.min(currentItems.length * 44 + 8, 300);
                            const top = spaceBelow < menuHeight && rect.top > menuHeight
                                ? rect.top - menuHeight - 4
                                : rect.bottom + 4;
                            const left = Math.min(rect.left, window.innerWidth - menuWidth - 8);
                            menu.style.top  = `${top}px`;
                            menu.style.left = `${Math.max(8, left)}px`;
                        };

                        return {
                            onStart(props) {
                                selectedIndex = 0;
                                currentItems = props.items;
                                currentCommand = props.command;
                                menu = document.createElement('div');
                                menu.className = 'slash-menu';
                                // No matches (e.g. caret re-enters "/sfdfdfd"):
                                // keep the empty container hidden, same as onUpdate.
                                menu.style.display = currentItems.length ? '' : 'none';
                                document.body.appendChild(menu);
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onUpdate(props) {
                                selectedIndex = 0;
                                currentItems = props.items;
                                currentCommand = props.command;
                                if (!menu) return; // onUpdate can fire after onExit
                                menu.style.display = currentItems.length ? '' : 'none';
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onKeyDown({ event }) {
                                if (!currentItems.length) return false;
                                if (event.key === 'ArrowUp') {
                                    event.preventDefault();
                                    selectedIndex = (selectedIndex - 1 + currentItems.length) % currentItems.length;
                                    renderItems();
                                    return true;
                                }
                                if (event.key === 'ArrowDown') {
                                    event.preventDefault();
                                    selectedIndex = (selectedIndex + 1) % currentItems.length;
                                    renderItems();
                                    return true;
                                }
                                if (event.key === 'Enter') {
                                    currentCommand?.(currentItems[selectedIndex]);
                                    return true;
                                }
                                return false;
                            },
                            onExit() {
                                menu?.remove();
                                menu = null;
                            },
                        };
                    },
                    command({ editor, range, props: item }) {
                        editor.chain().focus().deleteRange(range).run();
                        switch (item.id) {
                            case 'page':
                                dotnetRef.invokeMethodAsync('OnSlashCommandPage')
                                    .then(result => {
                                        if (!result) return;
                                        editor.chain().focus().insertContent({
                                            type: 'pageLink',
                                            attrs: { noteId: result.id, title: result.title },
                                        }).run();
                                    })
                                    .catch(err => console.error('[tiptap] OnSlashCommandPage failed', err));
                                break;
                            case 'h1':       editor.chain().focus().setHeading({ level: 1 }).run(); break;
                            case 'h2':       editor.chain().focus().setHeading({ level: 2 }).run(); break;
                            case 'h3':       editor.chain().focus().setHeading({ level: 3 }).run(); break;
                            case 'bullet':   editor.chain().focus().toggleBulletList().run(); break;
                            case 'numbered': editor.chain().focus().toggleOrderedList().run(); break;
                            case 'table':    editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(); break;
                            case 'quote':    editor.chain().focus().toggleBlockquote().run(); break;
                            case 'todo':     editor.chain().focus().toggleTaskList().run(); break;
                            case 'callout':  editor.chain().focus().insertContent({ type: 'callout', attrs: { emoji: '💡' }, content: [{ type: 'paragraph' }] }).run(); break;
                            case 'code':     editor.chain().focus().toggleCodeBlock().run(); break;
                            case 'mermaid':  editor.chain().focus().insertContent({ type: 'mermaid', attrs: { code: '' } }).run(); break;
                            case 'divider':  editor.chain().focus().setHorizontalRule().run(); break;
                            case 'toggle':
                                editor.chain().focus().insertContent({
                                    type: 'toggle',
                                    attrs: { open: true },
                                    content: [
                                        { type: 'toggleSummary' },
                                        { type: 'toggleContent', content: [{ type: 'paragraph' }] },
                                    ],
                                }).run();
                                // toggle start +1 = summary start, +1 = inside summary
                                placeCursorInInsertedSummary(editor, 'toggle', 2);
                                break;
                            case 'toggleh1':
                                editor.chain().focus().setHeading({ level: 1 })
                                    .updateAttributes('heading', { collapsible: true, open: true }).run();
                                break;
                            case 'toggleh2':
                                editor.chain().focus().setHeading({ level: 2 })
                                    .updateAttributes('heading', { collapsible: true, open: true }).run();
                                break;
                            case 'toggleh3':
                                editor.chain().focus().setHeading({ level: 3 })
                                    .updateAttributes('heading', { collapsible: true, open: true }).run();
                                break;
                            case 'accordion':
                                editor.chain().focus().insertContent({
                                    type: 'accordionGroup',
                                    content: [
                                        {
                                            type: 'toggle',
                                            attrs: { open: true },
                                            content: [
                                                { type: 'toggleSummary' },
                                                { type: 'toggleContent', content: [{ type: 'paragraph' }] },
                                            ],
                                        },
                                        {
                                            type: 'toggle',
                                            attrs: { open: false },
                                            content: [
                                                { type: 'toggleSummary' },
                                                { type: 'toggleContent', content: [{ type: 'paragraph' }] },
                                            ],
                                        },
                                    ],
                                }).run();
                                // group +1 = first toggle, +2 = its summary, +3 = inside it
                                placeCursorInInsertedSummary(editor, 'accordionGroup', 3);
                                break;
                        }
                    },
                }),
            ];
        },
    });
}
