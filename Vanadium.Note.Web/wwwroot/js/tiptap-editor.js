import { Editor, Node, Extension, mergeAttributes } from 'https://esm.sh/@tiptap/core@2'
import { PluginKey, Plugin, Selection, TextSelection } from 'https://esm.sh/prosemirror-state'
import { Decoration, DecorationSet } from 'https://esm.sh/prosemirror-view'
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2'
import Heading from 'https://esm.sh/@tiptap/extension-heading@2'
import BubbleMenu from 'https://esm.sh/@tiptap/extension-bubble-menu@2'
import Placeholder from 'https://esm.sh/@tiptap/extension-placeholder@2'
import Link from 'https://esm.sh/@tiptap/extension-link@2'
import Image from 'https://esm.sh/@tiptap/extension-image@2'
import Suggestion from 'https://esm.sh/@tiptap/suggestion@2'
import { Markdown } from 'https://esm.sh/tiptap-markdown?deps=@tiptap/core@2'
import TaskList from 'https://esm.sh/@tiptap/extension-task-list@2'
import TaskItem from 'https://esm.sh/@tiptap/extension-task-item@2'
import Table from 'https://esm.sh/@tiptap/extension-table@2'
import TableRow from 'https://esm.sh/@tiptap/extension-table-row@2'
import TableHeader from 'https://esm.sh/@tiptap/extension-table-header@2'
import TableCell from 'https://esm.sh/@tiptap/extension-table-cell@2'
import { createLowlight, common } from 'https://esm.sh/lowlight'
import CodeBlockLowlight from 'https://esm.sh/@tiptap/extension-code-block-lowlight@2'

const _editors = {};

// ── Mermaid lazy-loader ──────────────────────────────────────────────────────

let _mermaidPromise = null;

async function getMermaid() {
    if (!_mermaidPromise) {
        _mermaidPromise = import('https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.esm.min.mjs')
            .then(m => m.default)
            .catch(err => {
                _mermaidPromise = null;
                throw err;
            });
    }
    return _mermaidPromise;
}

// Render a mermaid diagram to SVG, applying the correct theme for the
// current light/dark mode.  Always re-initializes before rendering so
// that a mode switch mid-session is reflected without a page reload.
async function renderMermaidSvg(code) {
    const mermaid = await getMermaid();
    const isDark = document.body.classList.contains('dark-mode');
    mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: isDark ? 'dark' : 'default',
    });
    const id = `mermaid-${++_mermaidIdCounter}`;
    const { svg } = await mermaid.render(id, code);
    return svg;
}

// ── Code block with lowlight syntax highlighting ─────────────────────────────

const lowlight = createLowlight(common)

const CodeBlock = CodeBlockLowlight.extend({
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

// ── Tab / Shift-Tab handling ─────────────────────────────────────────────────

const TabIndent = Extension.create({
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

function createSlashCommandsExtension(dotnetRef) {
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
                                document.body.appendChild(menu);
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onUpdate(props) {
                                selectedIndex = 0;
                                currentItems = props.items;
                                currentCommand = props.command;
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

// ── FileAttachment node ──────────────────────────────────────────────────────

const FileAttachment = Node.create({
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

// ── PageLink node ────────────────────────────────────────────────────────────

const PageLink = Node.create({
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

// ── NoteMention node ────────────────────────────────────────────────────────

const NoteMention = Node.create({
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

// ── Mention extension (@) ────────────────────────────────────────────────────

function createMentionExtension(dotnetRef) {
    return Extension.create({
        name: 'mention',
        addProseMirrorPlugins() {
            return [
                Suggestion({
                    editor: this.editor,
                    pluginKey: new PluginKey('mention'),
                    char: '@',
                    allowSpaces: true,
                    items: async ({ query }) => {
                        try {
                            return await dotnetRef.invokeMethodAsync('SearchNotes', query);
                        } catch {
                            return [];
                        }
                    },
                    render() {
                        let menu = null;
                        let selectedIndex = 0;
                        let currentItems = [];
                        let currentCommand = null;

                        const renderItems = () => {
                            if (!menu) return;
                            menu.innerHTML = '';
                            if (!currentItems.length) {
                                const empty = document.createElement('div');
                                empty.className = 'mention-menu-empty';
                                empty.textContent = 'No notes found';
                                menu.appendChild(empty);
                                return;
                            }
                            currentItems.forEach((item, i) => {
                                const row = document.createElement('div');
                                row.className = 'mention-menu-item' + (i === selectedIndex ? ' mention-menu-item-active' : '');
                                row.innerHTML = `<span class="mention-menu-icon">📄</span><span class="mention-menu-title">${item.title || '(Untitled)'}</span>`;
                                row.addEventListener('mousedown', e => {
                                    e.preventDefault();
                                    currentCommand?.(item);
                                });
                                menu.appendChild(row);
                            });
                            menu.querySelector('.mention-menu-item-active')
                                ?.scrollIntoView({ block: 'nearest' });
                        };

                        const reposition = clientRect => {
                            if (!menu || !clientRect) return;
                            const rect = clientRect();
                            if (!rect) return;
                            const menuWidth = 240;
                            const itemCount = currentItems.length || 1;
                            const menuHeight = Math.min(itemCount * 36 + 8, 280);
                            const spaceBelow = window.innerHeight - rect.bottom;
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
                                menu.className = 'mention-menu';
                                document.body.appendChild(menu);
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onUpdate(props) {
                                selectedIndex = 0;
                                currentItems = props.items;
                                currentCommand = props.command;
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onKeyDown({ event }) {
                                if (event.key === 'ArrowUp') {
                                    if (!currentItems.length) return false;
                                    event.preventDefault();
                                    selectedIndex = (selectedIndex - 1 + currentItems.length) % currentItems.length;
                                    renderItems();
                                    return true;
                                }
                                if (event.key === 'ArrowDown') {
                                    if (!currentItems.length) return false;
                                    event.preventDefault();
                                    selectedIndex = (selectedIndex + 1) % currentItems.length;
                                    renderItems();
                                    return true;
                                }
                                if (event.key === 'Enter') {
                                    const item = currentItems[selectedIndex];
                                    if (item) { currentCommand?.(item); return true; }
                                    return false;
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
                        editor
                            .chain()
                            .focus()
                            .deleteRange(range)
                            .insertContent({
                                type: 'noteMention',
                                attrs: { noteId: item.id, title: item.title || '' },
                            })
                            .insertContent(' ')
                            .run();
                    },
                }),
                new Plugin({
                    key: new PluginKey('mentionClick'),
                    props: {
                        handleClick(view, pos, event) {
                            const mention = event.target?.closest?.('a.note-mention');
                            if (!mention) return false;
                            event.preventDefault();
                            const noteId = mention.getAttribute('data-note-id');
                            if (noteId) {
                                dotnetRef.invokeMethodAsync('OnMentionClick', noteId)
                                    .catch(err => console.error('[tiptap] OnMentionClick failed', err));
                            }
                            return true;
                        },
                    },
                }),
            ];
        },
    });
}

// ── Callout node ────────────────────────────────────────────────────────────

const Callout = Node.create({
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

// ── Mermaid node ─────────────────────────────────────────────────────────────

let _mermaidIdCounter = 0;

const MermaidNode = Node.create({
    name: 'mermaid',
    group: 'block',
    atom: true,

    addAttributes() {
        return {
            code: { default: '' },
        };
    },

    parseHTML() {
        return [{
            tag: 'pre[data-type="mermaid"]',
            getAttrs: (el) => ({
                code: el.querySelector('code')?.textContent ?? '',
            }),
        }];
    },

    renderHTML({ node }) {
        return [
            'pre',
            { 'data-type': 'mermaid' },
            ['code', {}, node.attrs.code],
        ];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            // ── DOM skeleton ──────────────────────────────────────────────
            const wrapper = document.createElement('div');
            wrapper.className = 'mermaid-block';

            const preview = document.createElement('div');
            preview.className = 'mermaid-preview';

            const editArea = document.createElement('div');
            editArea.className = 'mermaid-edit';
            editArea.style.display = 'none';

            const textarea = document.createElement('textarea');
            textarea.className = 'mermaid-textarea';
            textarea.placeholder = 'Enter Mermaid diagram code…\n\nExample:\ngraph TD\n  A-->B';
            textarea.spellcheck = false;

            const editActions = document.createElement('div');
            editActions.className = 'mermaid-edit-actions';

            const applyBtn = document.createElement('button');
            applyBtn.textContent = 'Apply';
            applyBtn.className = 'mermaid-apply-btn';

            const hint = document.createElement('span');
            hint.className = 'mermaid-hint';
            hint.textContent = 'Esc to cancel · Tab inserts spaces';

            editActions.appendChild(applyBtn);
            editActions.appendChild(hint);
            editArea.appendChild(textarea);
            editArea.appendChild(editActions);

            wrapper.appendChild(preview);
            wrapper.appendChild(editArea);

            let currentCode = node.attrs.code;
            let isEditing = false;

            // ── Render preview ────────────────────────────────────────────
            async function renderPreview(code) {
                if (!code.trim()) {
                    preview.innerHTML = '<span class="mermaid-placeholder">▦ Mermaid diagram · click to edit</span>';
                    return;
                }
                preview.innerHTML = '<span class="mermaid-placeholder mermaid-loading">Rendering…</span>';
                try {
                    preview.innerHTML = await renderMermaidSvg(code);
                } catch (err) {
                    preview.innerHTML = `<div class="mermaid-error">⚠ ${err.message || 'Syntax error'}</div>`;
                }
            }

            // ── Edit / preview toggle ─────────────────────────────────────
            function enterEdit() {
                if (isEditing) return;
                isEditing = true;
                textarea.value = currentCode;
                preview.style.display = 'none';
                editArea.style.display = '';
                // Rows auto-grow: at least 6, at most 24 lines
                textarea.rows = Math.min(24, Math.max(6, (currentCode.match(/\n/g) || []).length + 3));
                setTimeout(() => { textarea.focus(); textarea.setSelectionRange(textarea.value.length, textarea.value.length); }, 0);
            }

            function commitEdit() {
                if (!isEditing) return;
                isEditing = false;
                const newCode = textarea.value;
                preview.style.display = '';
                editArea.style.display = 'none';
                if (newCode !== currentCode) {
                    currentCode = newCode;
                    const pos = typeof getPos === 'function' ? getPos() : null;
                    if (pos !== null) {
                        editor.view.dispatch(
                            editor.state.tr.setNodeMarkup(pos, null, { ...node.attrs, code: newCode })
                        );
                    }
                }
                renderPreview(currentCode);
            }

            function cancelEdit() {
                if (!isEditing) return;
                isEditing = false;
                preview.style.display = '';
                editArea.style.display = 'none';
                textarea.value = currentCode;
            }

            // ── Event wiring ──────────────────────────────────────────────
            applyBtn.addEventListener('mousedown', (e) => {
                e.preventDefault();
                commitEdit();
            });

            textarea.addEventListener('keydown', (e) => {
                if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); return; }
                if (e.key === 'Tab') {
                    e.preventDefault();
                    const s = textarea.selectionStart;
                    const end = textarea.selectionEnd;
                    textarea.value = textarea.value.substring(0, s) + '    ' + textarea.value.substring(end);
                    textarea.selectionStart = textarea.selectionEnd = s + 4;
                }
            });

            // Auto-grow textarea rows on input
            textarea.addEventListener('input', () => {
                textarea.rows = Math.min(24, Math.max(6, (textarea.value.match(/\n/g) || []).length + 3));
            });

            preview.addEventListener('click', () => {
                if (editor.isEditable) enterEdit();
            });

            // Initial render; auto-enter edit mode if empty
            renderPreview(currentCode);
            if (!currentCode.trim()) {
                setTimeout(() => enterEdit(), 80);
            }

            return {
                dom: wrapper,

                update(updatedNode) {
                    if (updatedNode.type.name !== 'mermaid') return false;
                    if (updatedNode.attrs.code !== currentCode && !isEditing) {
                        currentCode = updatedNode.attrs.code;
                        renderPreview(currentCode);
                    }
                    return true;
                },

                // Swallow all events inside the edit area so ProseMirror doesn't steal them
                stopEvent(event) {
                    return editArea.contains(event.target);
                },

                // Prevent ProseMirror from reacting to DOM mutations inside this view
                ignoreMutation() { return true; },

                destroy() { /* nothing to clean up */ },
            };
        };
    },
});

// ── Toggle nodes ─────────────────────────────────────────────────────────────
//
// Collapsible content blocks (Notion-style toggles). Folding is CSS-only:
// the body stays in the document and in the serialized HTML, so search
// (ContentText) and orphan-file scanning are unaffected. All user-visible
// text lives in element text content, never in attributes (hard rule).
// Design doc: docs/plannings/note-toggle-feature.md

// Flip the `open` attribute of the node at pos. Fold state changes are
// excluded from undo history (NFR-4) and work in read-only mode too:
// `editable: false` only blocks user input, not programmatic transactions.
function toggleOpenAt(view, pos) {
    const node = view.state.doc.nodeAt(pos);
    if (!node) return;
    const tr = view.state.tr.setNodeMarkup(pos, null, { ...node.attrs, open: !node.attrs.open });
    tr.setMeta('addToHistory', false);
    view.dispatch(tr);
}

// Walk up from $pos and return the nearest ancestor of the given type.
function findAncestorOfType($pos, typeName) {
    for (let d = $pos.depth; d > 0; d--) {
        const node = $pos.node(d);
        if (node.type.name === typeName) {
            return { node, pos: $pos.before(d), depth: d };
        }
    }
    return null;
}

// Parent container. The content expression enforces exactly one summary
// followed by one body — invalid shapes are unrepresentable.
const Toggle = Node.create({
    name: 'toggle',
    group: 'block',
    content: 'toggleSummary toggleContent',
    defining: true,
    isolating: true,

    addAttributes() {
        return {
            open: {
                default: true,
                parseHTML: el => el.getAttribute('data-open') !== 'false',
                renderHTML: attrs => ({ 'data-open': attrs.open ? 'true' : 'false' }),
            },
        };
    },

    parseHTML() {
        return [
            { tag: 'div[data-type="toggle"]' },
            // Best-effort import of external/native HTML pastes.
            { tag: 'details', getAttrs: el => ({ open: el.hasAttribute('open') }) },
        ];
    },

    renderHTML({ HTMLAttributes }) {
        return ['div', mergeAttributes(HTMLAttributes, {
            'data-type': 'toggle',
            class: 'toggle-block',
        }), 0];
    },

    addNodeView() {
        return ({ node, view, getPos }) => {
            const dom = document.createElement('div');
            dom.className = 'toggle-block';
            dom.setAttribute('data-type', 'toggle');
            dom.setAttribute('data-open', node.attrs.open ? 'true' : 'false');

            const arrow = document.createElement('button');
            arrow.type = 'button';
            arrow.className = 'toggle-arrow';
            arrow.contentEditable = 'false';
            arrow.title = 'Toggle (Ctrl+Enter)';
            arrow.setAttribute('aria-expanded', String(node.attrs.open));
            arrow.textContent = '▸';

            const inner = document.createElement('div');
            inner.className = 'toggle-inner';

            dom.appendChild(arrow);
            dom.appendChild(inner);

            // Prevent the editor from stealing focus / moving the caret.
            arrow.addEventListener('mousedown', e => e.preventDefault());
            arrow.addEventListener('click', e => {
                e.preventDefault();
                const pos = typeof getPos === 'function' ? getPos() : null;
                if (pos == null) return;
                toggleOpenAt(view, pos);
            });

            return {
                dom,
                contentDOM: inner,

                update(updatedNode) {
                    if (updatedNode.type.name !== 'toggle') return false;
                    dom.setAttribute('data-open', updatedNode.attrs.open ? 'true' : 'false');
                    arrow.setAttribute('aria-expanded', String(updatedNode.attrs.open));
                    return true;
                },

                stopEvent(event) {
                    return event.target === arrow || arrow.contains(event.target);
                },

                ignoreMutation(mutation) {
                    if (mutation.type === 'selection') return false;
                    return mutation.target === arrow || arrow.contains(mutation.target);
                },
            };
        };
    },
});

// Summary line: inline content only, exactly one line.
const ToggleSummary = Node.create({
    name: 'toggleSummary',
    content: 'inline*',
    defining: true,
    selectable: false,

    parseHTML() {
        return [
            { tag: 'div[data-type="toggle-summary"]' },
            { tag: 'summary' }, // external <details> import
        ];
    },

    renderHTML() {
        return ['div', { 'data-type': 'toggle-summary', class: 'toggle-summary' }, 0];
    },
});

// Body: one or more blocks of any kind (incl. nested toggles/accordions).
const ToggleContent = Node.create({
    name: 'toggleContent',
    content: 'block+',
    defining: true,
    selectable: false,

    parseHTML() {
        return [{ tag: 'div[data-type="toggle-content"]' }];
    },

    renderHTML() {
        return ['div', { 'data-type': 'toggle-content', class: 'toggle-content' }, 0];
    },
});

// ── Collapsible heading ──────────────────────────────────────────────────────
//
// NOT a new node: the StarterKit Heading is extended with `collapsible` /
// `open` attributes (design decision D2). The document stays a flat sequence;
// the folded range is derived per render, never stored on the hidden nodes.
// A plain heading serializes exactly as before — zero churn for existing notes.

// Fold-range rule (§4.2): for heading H at child index i of parent P, the
// folded range covers children i+1 .. j-1, where j is the first subsequent
// child that is a heading with level <= H.level (or P's end).
// Returns per-sibling {from, to} spans for node decorations.
function headingFoldedSpans(doc, headingNode, headingPos) {
    const $pos = doc.resolve(headingPos);
    const parent = $pos.parent;
    const index = $pos.index($pos.depth);
    const spans = [];
    let childPos = headingPos + headingNode.nodeSize;
    for (let j = index + 1; j < parent.childCount; j++) {
        const child = parent.child(j);
        if (child.type.name === 'heading' && child.attrs.level <= headingNode.attrs.level) break;
        spans.push({ from: childPos, to: childPos + child.nodeSize });
        childPos += child.nodeSize;
    }
    return spans;
}

function makeHeadingFoldArrow(open) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'heading-fold-arrow';
    btn.contentEditable = 'false';
    btn.title = 'Fold section (Ctrl+Enter)';
    btn.setAttribute('aria-expanded', String(open));
    btn.textContent = '▸';
    return btn;
}

const headingFoldPlugin = new Plugin({
    key: new PluginKey('headingFold'),

    props: {
        decorations(state) {
            const decos = [];
            state.doc.descendants((node, pos) => {
                if (node.type.name !== 'heading' || !node.attrs.collapsible) return;
                decos.push(Decoration.widget(
                    pos + 1,
                    () => makeHeadingFoldArrow(node.attrs.open),
                    { side: -1, key: `hfold-${pos}-${node.attrs.open}` }
                ));
                if (node.attrs.open) return;
                for (const span of headingFoldedSpans(state.doc, node, pos)) {
                    decos.push(Decoration.node(span.from, span.to, { class: 'heading-folded' }));
                }
            });
            return DecorationSet.create(state.doc, decos);
        },

        // mousedown (not click) so the caret never jumps into the heading first.
        // handleDOMEvents also fires with editable=false, so folding works in
        // read-only mode (FR-H6).
        handleDOMEvents: {
            mousedown(view, event) {
                const arrowEl = event.target?.closest?.('.heading-fold-arrow');
                if (!arrowEl) return false;
                event.preventDefault();
                const headingDom = arrowEl.closest('h1,h2,h3,h4,h5,h6');
                if (!headingDom) return true;
                const headingPos = view.posAtDOM(headingDom, 0) - 1;
                const node = view.state.doc.nodeAt(headingPos);
                if (node?.type.name === 'heading' && node.attrs.collapsible) {
                    toggleOpenAt(view, headingPos);
                }
                return true;
            },
        },
    },

    // Selection guard (FR-H5): if a transaction leaves the cursor inside a
    // folded range, skip it past (or before, when moving backwards) the range.
    // Deleting a folded heading needs no cleanup: decorations are recomputed
    // from the new doc, so its hidden range simply un-hides.
    appendTransaction(transactions, oldState, newState) {
        if (!transactions.some(tr => tr.docChanged || tr.selectionSet)) return null;
        const sel = newState.selection;
        if (!sel.empty) return null;

        let fix = null;
        newState.doc.descendants((node, pos) => {
            if (fix) return false;
            if (node.type.name !== 'heading' || !node.attrs.collapsible || node.attrs.open) return;
            for (const span of headingFoldedSpans(newState.doc, node, pos)) {
                if (sel.from > span.from && sel.from < span.to) {
                    const backwards = sel.from < oldState.selection.from;
                    const target = backwards ? span.from : span.to;
                    fix = Selection.near(newState.doc.resolve(target), backwards ? -1 : 1);
                    return false;
                }
            }
        });

        return fix ? newState.tr.setSelection(fix) : null;
    },
});

const CollapsibleHeading = Heading.extend({
    addAttributes() {
        return {
            ...this.parent?.(),
            collapsible: {
                default: false,
                parseHTML: el => el.getAttribute('data-collapsible') === 'true',
                renderHTML: attrs => attrs.collapsible ? { 'data-collapsible': 'true' } : {},
            },
            open: {
                default: true,
                parseHTML: el => el.getAttribute('data-open') !== 'false',
                renderHTML: attrs => attrs.collapsible && !attrs.open ? { 'data-open': 'false' } : {},
            },
        };
    },

    addProseMirrorPlugins() {
        return [...(this.parent?.() ?? []), headingFoldPlugin];
    },
});

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

const AccordionGroup = Node.create({
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

// ── Toggle keymap ────────────────────────────────────────────────────────────

// True when the toggle's body is exactly one empty paragraph.
function isEmptyToggleBody(toggleNode) {
    const body = toggleNode.child(1);
    return body.childCount === 1
        && body.firstChild.type.name === 'paragraph'
        && body.firstChild.content.size === 0;
}

const ToggleKeymap = Extension.create({
    name: 'toggleKeymap',
    priority: 1000, // must win over StarterKit's default Enter/Backspace

    addKeyboardShortcuts() {
        return {
            // Flip the nearest ancestor toggle from anywhere inside it, or the
            // collapsible heading the cursor is on.
            'Mod-Enter': ({ editor }) => {
                const { $from } = editor.state.selection;
                const toggle = findAncestorOfType($from, 'toggle');
                if (toggle) {
                    toggleOpenAt(editor.view, toggle.pos);
                    return true;
                }
                if ($from.parent.type.name === 'heading' && $from.parent.attrs.collapsible) {
                    toggleOpenAt(editor.view, $from.before($from.depth));
                    return true;
                }
                return false;
            },

            'Enter': ({ editor }) => {
                const { state, view } = editor;
                const { $from, empty } = state.selection;
                if (!empty) return false;

                if ($from.parent.type.name === 'toggleSummary') {
                    const toggle = findAncestorOfType($from, 'toggle');
                    if (!toggle) return false;
                    const group = findAncestorOfType($from, 'accordionGroup');
                    const inGroup = group && group.depth === toggle.depth - 1;

                    // (c) Summary of the LAST toggle of an accordion whose body
                    // is empty: append a new closed toggle to the group (FR-A4).
                    if (inGroup) {
                        const toggleEnd = toggle.pos + toggle.node.nodeSize;
                        const groupContentEnd = group.pos + group.node.nodeSize - 1;
                        if (toggleEnd === groupContentEnd && isEmptyToggleBody(toggle.node)) {
                            const newToggle = state.schema.nodes.toggle.createAndFill({ open: false });
                            if (!newToggle) return false;
                            const tr = state.tr.insert(groupContentEnd, newToggle);
                            tr.setSelection(TextSelection.create(tr.doc, groupContentEnd + 2));
                            view.dispatch(tr.scrollIntoView());
                            return true;
                        }
                    }

                    // (a) Move the cursor to the start of the body, opening the
                    // toggle if it is closed.
                    const tr = state.tr;
                    if (!toggle.node.attrs.open) {
                        tr.setNodeMarkup(toggle.pos, null, { ...toggle.node.attrs, open: true });
                        tr.setMeta('addToHistory', false);
                    }
                    const bodyPos = toggle.pos + 1 + toggle.node.child(0).nodeSize;
                    tr.setSelection(Selection.near(tr.doc.resolve(bodyPos + 1), 1));
                    view.dispatch(tr.scrollIntoView());
                    return true;
                }

                // (b) Trailing empty paragraph of a toggle body exits the toggle:
                // the empty paragraph is lifted below the toggle (below the whole
                // group when the toggle is an accordion item).
                if ($from.parent.type.name === 'paragraph' && $from.parent.content.size === 0) {
                    const bodyDepth = $from.depth - 1;
                    if (bodyDepth < 1 || $from.node(bodyDepth).type.name !== 'toggleContent') return false;
                    const body = $from.node(bodyDepth);
                    const toggle = findAncestorOfType($from, 'toggle');
                    if (!toggle) return false;
                    const isLast = $from.after($from.depth) === $from.end(bodyDepth);
                    if (!isLast) return false;

                    const group = findAncestorOfType($from, 'accordionGroup');
                    const container = group && group.depth === toggle.depth - 1 ? group : toggle;

                    const tr = state.tr;
                    if (body.childCount > 1) {
                        tr.delete($from.before($from.depth), $from.after($from.depth));
                    }
                    const afterPos = tr.mapping.map(container.pos + container.node.nodeSize);
                    tr.insert(afterPos, state.schema.nodes.paragraph.create());
                    tr.setSelection(TextSelection.create(tr.doc, afterPos + 1));
                    view.dispatch(tr.scrollIntoView());
                    return true;
                }

                return false;
            },

            // At the start of an empty summary: unwrap the toggle — its body
            // blocks are lifted to the toggle's level, the summary is dropped.
            // Accordion items lift outside the group (toggle+ forbids plain
            // blocks inside); a single-item group dissolves entirely.
            'Backspace': ({ editor }) => {
                const { state, view } = editor;
                const { $from, empty } = state.selection;
                if (!empty) return false;
                if ($from.parent.type.name !== 'toggleSummary') return false;
                if ($from.parent.content.size !== 0 || $from.parentOffset !== 0) return false;
                const toggle = findAncestorOfType($from, 'toggle');
                if (!toggle) return false;

                const bodyChildren = toggle.node.child(1).content;
                const group = findAncestorOfType($from, 'accordionGroup');
                const inGroup = group && group.depth === toggle.depth - 1;

                const tr = state.tr;
                if (inGroup && group.node.childCount === 1) {
                    // Last item: dissolve the group too (FR-A5).
                    tr.replaceWith(group.pos, group.pos + group.node.nodeSize, bodyChildren);
                    tr.setSelection(Selection.near(tr.doc.resolve(group.pos), 1));
                } else if (inGroup) {
                    const isFirst = group.pos + 1 === toggle.pos;
                    tr.delete(toggle.pos, toggle.pos + toggle.node.nodeSize);
                    const insertAt = isFirst ? group.pos : tr.mapping.map(group.pos + group.node.nodeSize);
                    tr.insert(insertAt, bodyChildren);
                    tr.setSelection(Selection.near(tr.doc.resolve(insertAt), 1));
                } else {
                    tr.replaceWith(toggle.pos, toggle.pos + toggle.node.nodeSize, bodyChildren);
                    tr.setSelection(Selection.near(tr.doc.resolve(toggle.pos), 1));
                }
                view.dispatch(tr.scrollIntoView());
                return true;
            },
        };
    },
});

// ── Callout emoji picker ─────────────────────────────────────────────────────

const CALLOUT_EMOJIS = ['💡', 'ℹ️', '⚠️', '🚨', '✅', '📝', '💬', '🔥', '📌', '🎯'];

let _calloutPicker = null;

function hideCalloutPicker() {
    _calloutPicker?.remove();
    _calloutPicker = null;
}

function showCalloutEmojiPicker(emojiEl, editor) {
    hideCalloutPicker();

    _calloutPicker = document.createElement('div');
    _calloutPicker.className = 'callout-emoji-picker';

    for (const emoji of CALLOUT_EMOJIS) {
        const btn = document.createElement('button');
        btn.textContent = emoji;
        btn.className = 'callout-emoji-btn';
        btn.addEventListener('mousedown', (e) => {
            e.preventDefault();
            const calloutEl = emojiEl.closest('[data-type="callout"]');
            if (calloutEl) {
                let found = null;
                editor.state.doc.descendants((node, pos) => {
                    if (found !== null) return false;
                    if (node.type.name === 'callout' && editor.view.nodeDOM(pos) === calloutEl) {
                        found = pos;
                        return false;
                    }
                });
                if (found !== null) {
                    editor.view.dispatch(
                        editor.state.tr.setNodeMarkup(found, null, {
                            ...editor.state.doc.nodeAt(found).attrs,
                            emoji,
                        })
                    );
                }
            }
            hideCalloutPicker();
        });
        _calloutPicker.appendChild(btn);
    }

    document.body.appendChild(_calloutPicker);

    const rect = emojiEl.getBoundingClientRect();
    _calloutPicker.style.top  = `${rect.bottom + 4}px`;
    _calloutPicker.style.left = `${Math.max(8, rect.left)}px`;
}

document.addEventListener('mousedown', (e) => {
    if (_calloutPicker && !_calloutPicker.contains(e.target) && !e.target.classList.contains('callout-emoji')) {
        hideCalloutPicker();
    }
});

// ── Upload helpers ───────────────────────────────────────────────────────────

function uploadWithProgress(url, formData, headers, onProgress) {
    return new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', url);
        for (const [key, value] of Object.entries(headers)) {
            xhr.setRequestHeader(key, value);
        }
        xhr.upload.addEventListener('progress', (e) => {
            if (e.lengthComputable) onProgress(e.loaded / e.total);
        });
        xhr.addEventListener('load', () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                resolve(JSON.parse(xhr.responseText));
            } else {
                reject(new Error(`Upload failed: ${xhr.status}`));
            }
        });
        xhr.addEventListener('error', () => reject(new Error('Network error')));
        xhr.send(formData);
    });
}

function createProgressToast(filename) {
    const toast = document.createElement('div');
    toast.className = 'upload-toast';
    toast.innerHTML = `
        <span class="upload-toast-name">${filename}</span>
        <div class="upload-toast-bar-wrap"><div class="upload-toast-bar"></div></div>
        <span class="upload-toast-pct">0%</span>
    `;
    document.body.appendChild(toast);

    return {
        update(ratio) {
            toast.querySelector('.upload-toast-bar').style.width = `${Math.round(ratio * 100)}%`;
            toast.querySelector('.upload-toast-pct').textContent = `${Math.round(ratio * 100)}%`;
        },
        done() {
            toast.classList.add('upload-toast-done');
            setTimeout(() => toast.remove(), 1200);
        },
        error() {
            toast.classList.add('upload-toast-error');
            setTimeout(() => toast.remove(), 3000);
        },
    };
}

// ── Link popover ────────────────────────────────────────────────────────────

function createLinkPopover(editorId) {
    const popover = document.createElement('div');
    popover.className = 'tiptap-link-popover';

    const input = document.createElement('input');
    input.type = 'url';
    input.placeholder = 'https://';
    input.className = 'link-input';

    const confirmBtn = document.createElement('button');
    confirmBtn.textContent = '↵';
    confirmBtn.title = 'Apply (Enter)';
    confirmBtn.className = 'link-btn link-confirm';

    const removeBtn = document.createElement('button');
    removeBtn.textContent = '✕';
    removeBtn.title = 'Remove link';
    removeBtn.className = 'link-btn link-remove';

    popover.appendChild(input);
    popover.appendChild(confirmBtn);
    popover.appendChild(removeBtn);
    popover.style.display = 'none';
    document.body.appendChild(popover);

    function applyLink() {
        const entry = _editors[editorId];
        if (!entry) return;
        const raw = input.value.trim();
        if (raw) {
            const href = /^https?:\/\//i.test(raw) ? raw : 'https://' + raw;
            const to = entry.editor.state.selection.to;
            entry.editor.chain().focus().setLink({ href }).setTextSelection(to).run();
        }
        hide();
    }

    function hide() {
        popover.style.display = 'none';
        input.value = '';
    }

    confirmBtn.addEventListener('mousedown', (e) => { e.preventDefault(); applyLink(); });

    removeBtn.addEventListener('mousedown', (e) => {
        e.preventDefault();
        _editors[editorId]?.editor.chain().focus().unsetLink().run();
        hide();
    });

    input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter')  { e.preventDefault(); applyLink(); }
        if (e.key === 'Escape') { _editors[editorId]?.editor.commands.focus(); hide(); }
    });

    // Click outside → close
    document.addEventListener('mousedown', (e) => {
        if (popover.style.display !== 'none' && !popover.contains(e.target)) {
            _editors[editorId]?.editor.commands.focus();
            hide();
        }
    });

    return { popover, input, hide };
}

function showLinkPopover(editorId) {
    const entry = _editors[editorId];
    if (!entry) return;

    const { popover, input } = entry.linkPopover;

    // Pre-fill if selection is already a link
    input.value = entry.editor.getAttributes('link').href || '';

    // Position below the current selection
    const sel = window.getSelection();
    if (sel.rangeCount > 0) {
        const rect = sel.getRangeAt(0).getBoundingClientRect();
        popover.style.display = 'flex';
        // Clamp to viewport right edge
        const popoverWidth = 320;
        const left = Math.min(rect.left, window.innerWidth - popoverWidth - 8);
        popover.style.top  = `${rect.bottom + 8}px`;
        popover.style.left = `${Math.max(8, left)}px`;
    }

    setTimeout(() => input.focus(), 0);
}

// ── Bubble menu ─────────────────────────────────────────────────────────────

function createBubbleMenu(editorId) {
    const menu = document.createElement('div');
    menu.className = 'tiptap-bubble-menu';

    const items = [
        { label: 'B',  cmd: 'bold',        title: 'Bold (Ctrl+B)' },
        { label: 'I',  cmd: 'italic',       title: 'Italic (Ctrl+I)' },
        { label: 'S',  cmd: 'strike',       title: 'Strikethrough' },
        { sep: true },
        { label: 'H1', cmd: 'heading1',     title: 'Heading 1' },
        { label: 'H2', cmd: 'heading2',     title: 'Heading 2' },
        { label: 'H3', cmd: 'heading3',     title: 'Heading 3' },
        { sep: true },
        { label: '•',  cmd: 'bulletList',   title: 'Bullet list' },
        { label: '1.', cmd: 'orderedList',  title: 'Numbered list' },
        { label: '"',  cmd: 'blockquote',   title: 'Blockquote' },
        { label: '<>', cmd: 'code',         title: 'Inline code' },
        { sep: true },
        { label: '🔗', cmd: 'link',         title: 'Link (Ctrl+K)' },
    ];

    for (const item of items) {
        if (item.sep) {
            const sep = document.createElement('span');
            sep.className = 'bubble-sep';
            menu.appendChild(sep);
        } else {
            const btn = document.createElement('button');
            btn.textContent = item.label;
            btn.title = item.title;
            btn.addEventListener('mousedown', (e) => {
                e.preventDefault();
                if (item.cmd === 'link') {
                    showLinkPopover(editorId);
                } else {
                    runCommand(_editors[editorId]?.editor, item.cmd);
                }
            });
            menu.appendChild(btn);
        }
    }

    document.body.appendChild(menu);
    return menu;
}

function runCommand(editor, cmd) {
    if (!editor) return;
    const c = editor.chain().focus();
    switch (cmd) {
        case 'bold':        c.toggleBold().run(); break;
        case 'italic':      c.toggleItalic().run(); break;
        case 'strike':      c.toggleStrike().run(); break;
        case 'heading1':    c.toggleHeading({ level: 1 }).run(); break;
        case 'heading2':    c.toggleHeading({ level: 2 }).run(); break;
        case 'heading3':    c.toggleHeading({ level: 3 }).run(); break;
        case 'bulletList':  c.toggleBulletList().run(); break;
        case 'orderedList': c.toggleOrderedList().run(); break;
        case 'blockquote':  c.toggleBlockquote().run(); break;
        case 'code':        c.toggleCode().run(); break;
    }
}

// ── Authenticated image loading ───────────────────────────────────────────────
//
// After Tiptap renders content, <img> tags carry the original /api/images/{id}
// URLs.  The browser cannot load those directly now that GET requires a JWT.
// This function fetches each matching image with the Authorization header,
// creates an object URL from the response blob, and swaps it into the DOM src.
// The ProseMirror model is deliberately NOT touched — editor.getHTML() still
// returns the canonical API URL, which is what gets persisted.
//
// blobUrls is a per-editor array; callers are responsible for revoking its
// entries when the editor is destroyed or content is replaced.

async function resolveAuthenticatedImages(editorDom, apiBaseUrl, authToken, blobUrls) {
    const prefix = `${apiBaseUrl}/api/images/`;
    const headers = authToken ? { 'Authorization': `Bearer ${authToken}` } : {};
    const imgs = editorDom.querySelectorAll(`img[src^="${CSS.escape(prefix)}"], img[src^="${prefix}"]`);

    for (const img of imgs) {
        const src = img.getAttribute('src');
        if (!src || !src.startsWith(prefix)) continue;

        try {
            const response = await fetch(src, { headers });
            if (!response.ok) {
                console.warn(`[tiptap] Image auth fetch failed: ${src} (HTTP ${response.status})`);
                continue;
            }
            const blob = await response.blob();
            const blobUrl = URL.createObjectURL(blob);
            blobUrls.push(blobUrl);
            img.src = blobUrl;
        } catch (err) {
            console.error('[tiptap] Image fetch error', { src, error: err.message });
        }
    }
}

// ── Public API ───────────────────────────────────────────────────────────────

window.tiptapInterop = {
    async init(elementId, dotnetRef, initialContent, apiBaseUrl, authToken, editable = true) {
        const el = document.getElementById(elementId);
        if (!el) {
            console.error(`[tiptap] Element not found: '${elementId}'`);
            return;
        }

        const bubbleMenuEl = createBubbleMenu(elementId);
        const linkPopover  = createLinkPopover(elementId);

        const editor = new Editor({
            element: el,
            extensions: [
                StarterKit.configure({ codeBlock: false, heading: false }),
                CollapsibleHeading.configure({ levels: [1, 2, 3, 4, 5, 6] }),
                CodeBlock.configure({ lowlight, defaultLanguage: 'plaintext' }),
                Markdown.configure({
                    html: true,
                    tightLists: true,
                    bulletListMarker: '-',
                    linkify: false,
                    breaks: false,
                    transformPastedText: true,
                    transformCopiedText: false,
                }),
                Placeholder.configure({
                    // includeChildren + showOnlyCurrent:false so empty toggle
                    // summaries always show their hint; CSS only renders the
                    // ::before for the first paragraph and toggle summaries,
                    // so other empty nodes are visually unchanged.
                    includeChildren: true,
                    showOnlyCurrent: false,
                    placeholder: ({ node }) =>
                        node.type.name === 'toggleSummary' ? 'Toggle summary' : 'Write something...',
                }),
                Link.configure({
                    autolink: true,
                    openOnClick: false,
                    HTMLAttributes: { target: '_blank', rel: 'noopener noreferrer' },
                }),
                Image.configure({
                    inline: false,
                    HTMLAttributes: { class: 'tiptap-image' },
                }),
                TaskList,
                TaskItem.configure({ nested: true }),
                Table.configure({ resizable: false }),
                TableRow,
                TableHeader,
                TableCell,
                TabIndent,
                FileAttachment,
                PageLink,
                NoteMention,
                Callout,
                MermaidNode,
                Toggle,
                ToggleSummary,
                ToggleContent,
                AccordionGroup,
                ToggleKeymap,
                createSlashCommandsExtension(dotnetRef),
                createMentionExtension(dotnetRef),
                BubbleMenu.configure({
                    element: bubbleMenuEl,
                    shouldShow: ({ editor, from, to }) => editor.isFocused && from !== to,
                }),
            ],
            content: initialContent || '',
            editable,
            onUpdate({ editor }) {
                dotnetRef.invokeMethodAsync('OnEditorContentChanged', editor.getHTML())
                    .catch(err => console.error('[tiptap] OnEditorContentChanged failed', err));
            },
        });

        // Ctrl+K shortcut
        editor.view.dom.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                showLinkPopover(elementId);
            }
        });

        // Clipboard image paste — upload to server, insert URL
        editor.view.dom.addEventListener('paste', async (e) => {
            const items = e.clipboardData?.items;
            if (!items) return;
            for (const item of items) {
                if (item.type.startsWith('image/')) {
                    e.preventDefault();
                    const file = item.getAsFile();
                    if (!file) return;

                    const formData = new FormData();
                    formData.append('file', file);
                    const headers = authToken ? { 'Authorization': `Bearer ${authToken}` } : {};
                    const toast = createProgressToast(file.name || 'image');

                    try {
                        const { url } = await uploadWithProgress(
                            `${apiBaseUrl}/api/images`, formData, headers,
                            (ratio) => toast.update(ratio)
                        );
                        toast.done();
                        console.log(`[tiptap] Image pasted and uploaded: ${file.name} (${file.size}B)`);
                        editor.chain().focus().setImage({ src: `${apiBaseUrl}${url}` }).run();
                        // The browser cannot load /api/images/* without a JWT, so resolve
                        // the newly inserted <img> to a Blob URL immediately after insertion.
                        const entry = _editors[elementId];
                        if (entry) {
                            await resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, authToken, entry.blobUrls);
                        }
                    } catch (err) {
                        toast.error();
                        console.error('[tiptap] Paste image upload failed', { file: file.name, size: file.size, error: err.message });
                    }
                    break;
                }
            }
        });

        // File attachment click — ProseMirror intercepts clicks, so we handle it manually
        editor.view.dom.addEventListener('click', (e) => {
            const link = e.target.closest('a.file-attachment');
            if (!link) return;
            e.preventDefault();
            e.stopPropagation();
            const a = document.createElement('a');
            a.href = link.getAttribute('href');
            a.download = link.getAttribute('data-filename') || '';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        });

        // Callout emoji click — prevent cursor move, open emoji picker
        editor.view.dom.addEventListener('mousedown', (e) => {
            if (e.target.classList.contains('callout-emoji')) e.preventDefault();
        });

        editor.view.dom.addEventListener('click', (e) => {
            const emojiEl = e.target.closest('.callout-emoji');
            if (!emojiEl) return;
            showCalloutEmojiPicker(emojiEl, editor);
        });

        // Page link click — open sub-note in dialog via Blazor
        editor.view.dom.addEventListener('click', (e) => {
            const block = e.target.closest('.page-link-block');
            if (!block) return;
            e.preventDefault();
            e.stopPropagation();
            const noteId = block.getAttribute('data-note-id');
            if (noteId) {
                // Blur the editor before the async dialog opens so the bubble menu hides immediately
                editor.commands.blur();
                dotnetRef.invokeMethodAsync('OnPageLinkClick', noteId)
                    .catch(err => console.error('[tiptap] OnPageLinkClick failed', err));
            }
        });

        // File drag & drop
        editor.view.dom.addEventListener('dragover', (e) => {
            if (e.dataTransfer?.types.includes('Files')) {
                e.preventDefault();
                editor.view.dom.classList.add('drag-over');
            }
        });

        editor.view.dom.addEventListener('dragleave', (e) => {
            if (!editor.view.dom.contains(e.relatedTarget)) {
                editor.view.dom.classList.remove('drag-over');
            }
        });

        editor.view.dom.addEventListener('drop', async (e) => {
            editor.view.dom.classList.remove('drag-over');
            const files = Array.from(e.dataTransfer?.files || []);
            if (files.length === 0) return;
            e.preventDefault();

            const pos = editor.view.posAtCoords({ left: e.clientX, top: e.clientY });
            const insertPos = pos ? pos.pos : editor.state.doc.content.size;

            const headers = authToken ? { 'Authorization': `Bearer ${authToken}` } : {};

            for (const file of files) {
                const formData = new FormData();
                formData.append('file', file);
                const toast = createProgressToast(file.name);

                try {
                    if (file.type.startsWith('image/')) {
                        // Image files are inserted as inline images
                        const { url } = await uploadWithProgress(
                            `${apiBaseUrl}/api/images`, formData, headers,
                            (ratio) => toast.update(ratio)
                        );
                        toast.done();
                        console.log(`[tiptap] Image dropped and uploaded: ${file.name} (${file.size}B) -> ${url}`);
                        editor.chain().focus().insertContentAt(insertPos, {
                            type: 'image',
                            attrs: { src: `${apiBaseUrl}${url}`, class: 'tiptap-image' },
                        }).run();
                        // Resolve the newly inserted <img> to a Blob URL immediately.
                        const entry = _editors[elementId];
                        if (entry) {
                            await resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, authToken, entry.blobUrls);
                        }
                    } else {
                        // Other files are inserted as file attachment links
                        const { url, filename } = await uploadWithProgress(
                            `${apiBaseUrl}/api/files`, formData, headers,
                            (ratio) => toast.update(ratio)
                        );
                        toast.done();
                        console.log(`[tiptap] File dropped and uploaded: ${file.name} (${file.size}B) -> ${url}`);
                        editor.chain().focus().insertContentAt(insertPos, {
                            type: 'fileAttachment',
                            attrs: { href: `${apiBaseUrl}${url}`, filename },
                        }).run();
                    }
                } catch (err) {
                    toast.error();
                    console.error('[tiptap] Drop file upload failed', { file: file.name, size: file.size, error: err.message });
                }
            }
        });

        const blobUrls = [];
        _editors[elementId] = { editor, bubbleMenuEl, linkPopover, apiBaseUrl, authToken, blobUrls };
        console.log(`[tiptap] Editor initialized: ${elementId}`);

        // Resolve any images that were injected with initialContent.
        if (initialContent) {
            await resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, authToken, blobUrls);
        }
    },

    focus(elementId) {
        _editors[elementId]?.editor.commands.focus('start');
    },

    // Toggle read-only mode (used for archived notes).
    setEditable(elementId, editable) {
        _editors[elementId]?.editor.setEditable(editable);
    },

    async setContent(elementId, content) {
        const entry = _editors[elementId];
        if (!entry) return;
        // Revoke previous blob URLs before the DOM is replaced with new content.
        for (const url of entry.blobUrls) URL.revokeObjectURL(url);
        entry.blobUrls = [];
        entry.editor.commands.setContent(content, false);
        await resolveAuthenticatedImages(entry.editor.view.dom, entry.apiBaseUrl, entry.authToken, entry.blobUrls);
    },

    setInputValue(elementId, value) {
        const el = document.getElementById(elementId);
        if (el) el.value = value;
    },

    updateMentionTitle(elementId, noteId, newTitle) {
        const entry = _editors[elementId];
        if (!entry) return null;
        const { editor } = entry;
        let found = false;
        editor.state.doc.descendants((node, pos) => {
            if (found) return false;
            if (node.type.name === 'noteMention' && node.attrs.noteId === noteId) {
                editor.view.dispatch(
                    editor.state.tr.setNodeMarkup(pos, null, { ...node.attrs, title: newTitle })
                );
                found = true;
                return false;
            }
        });
        return found ? editor.getHTML() : null;
    },

    updatePageLink(elementId, noteId, newTitle) {
        const entry = _editors[elementId];
        if (!entry) return null;
        const { editor } = entry;
        let found = false;
        editor.state.doc.descendants((node, pos) => {
            if (found) return false;
            if (node.type.name === 'pageLink' && node.attrs.noteId === noteId) {
                editor.view.dispatch(
                    editor.state.tr.setNodeMarkup(pos, null, { ...node.attrs, title: newTitle })
                );
                found = true;
                return false;
            }
        });
        // Read HTML from the updated state
        return found ? editor.getHTML() : null;
    },

    getMarkdown(elementId) {
        return _editors[elementId]?.editor.storage.markdown.getMarkdown() ?? '';
    },

    downloadMarkdown(elementId, filename) {
        const md = _editors[elementId]?.editor.storage.markdown.getMarkdown() ?? '';
        const blob = new Blob([md], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },

    // Render all mermaid blocks found inside a DOM element (for read-only views).
    // Pass a CSS selector string or a DOM element reference.
    async renderMermaidIn(root) {
        const el = typeof root === 'string' ? document.querySelector(root) : root;
        if (!el) return;
        const blocks = el.querySelectorAll('pre[data-type="mermaid"]');
        if (!blocks.length) return;
        // Eagerly load mermaid so any module-load error surfaces early
        try { await getMermaid(); } catch (err) {
            console.error('[tiptap] Failed to load Mermaid for read-only render', err);
            return;
        }
        for (const block of blocks) {
            const code = block.querySelector('code')?.textContent?.trim();
            if (!code) continue;
            try {
                const container = document.createElement('div');
                container.className = 'mermaid-rendered';
                container.innerHTML = await renderMermaidSvg(code);
                block.replaceWith(container);
            } catch (err) {
                const errDiv = document.createElement('div');
                errDiv.className = 'mermaid-error';
                errDiv.textContent = `⚠ ${err.message || 'Diagram error'}`;
                block.replaceWith(errDiv);
            }
        }
    },

    destroy(elementId) {
        const entry = _editors[elementId];
        if (entry) {
            hideCalloutPicker();
            for (const url of entry.blobUrls) URL.revokeObjectURL(url);
            entry.editor.destroy();
            entry.bubbleMenuEl.remove();
            entry.linkPopover.popover.remove();
            delete _editors[elementId];
            console.log(`[tiptap] Editor destroyed: ${elementId}`);
        }
    },
};
