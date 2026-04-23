import { Editor, Node, Extension, mergeAttributes } from 'https://esm.sh/@tiptap/core@2'
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2'
import BubbleMenu from 'https://esm.sh/@tiptap/extension-bubble-menu@2'
import Placeholder from 'https://esm.sh/@tiptap/extension-placeholder@2'
import Link from 'https://esm.sh/@tiptap/extension-link@2'
import Image from 'https://esm.sh/@tiptap/extension-image@2'
import Suggestion from 'https://esm.sh/@tiptap/suggestion@2'

const _editors = {};

// ── Slash commands ───────────────────────────────────────────────────────────

const SLASH_COMMANDS = [
    { id: 'page',     label: 'New Page',      desc: 'Create a sub-note',    icon: '📄', keywords: ['page', 'subnote'] },
    { id: 'h1',       label: 'Heading 1',     desc: 'Large heading',        icon: 'H1', keywords: ['heading'] },
    { id: 'h2',       label: 'Heading 2',     desc: 'Medium heading',       icon: 'H2', keywords: ['heading'] },
    { id: 'h3',       label: 'Heading 3',     desc: 'Small heading',        icon: 'H3', keywords: ['heading'] },
    { id: 'bullet',   label: 'Bullet List',   desc: 'Unordered list',       icon: '•',  keywords: ['list', 'ul'] },
    { id: 'numbered', label: 'Numbered List', desc: 'Ordered list',         icon: '1.', keywords: ['list', 'ol'] },
    { id: 'quote',    label: 'Quote',         desc: 'Block quotation',      icon: '"',  keywords: ['blockquote'] },
    { id: 'code',     label: 'Code Block',    desc: 'Monospace code block', icon: '</>', keywords: ['codeblock'] },
    { id: 'divider',  label: 'Divider',       desc: 'Horizontal rule',      icon: '—',  keywords: ['hr', 'rule'] },
];

function createSlashCommandsExtension(dotnetRef) {
    return Extension.create({
        name: 'slashCommands',
        addProseMirrorPlugins() {
            return [
                Suggestion({
                    editor: this.editor,
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
                            case 'quote':    editor.chain().focus().toggleBlockquote().run(); break;
                            case 'code':     editor.chain().focus().toggleCodeBlock().run(); break;
                            case 'divider':  editor.chain().focus().setHorizontalRule().run(); break;
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

// ── Public API ───────────────────────────────────────────────────────────────

window.tiptapInterop = {
    init(elementId, dotnetRef, initialContent, apiBaseUrl, authToken) {
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
                StarterKit,
                Placeholder.configure({ placeholder: 'Write something...' }),
                Link.configure({
                    autolink: true,
                    openOnClick: false,
                    HTMLAttributes: { target: '_blank', rel: 'noopener noreferrer' },
                }),
                Image.configure({
                    inline: false,
                    HTMLAttributes: { class: 'tiptap-image' },
                }),
                FileAttachment,
                PageLink,
                createSlashCommandsExtension(dotnetRef),
                BubbleMenu.configure({
                    element: bubbleMenuEl,
                    shouldShow: ({ from, to }) => from !== to,
                }),
            ],
            content: initialContent || '',
            onUpdate({ editor }) {
                dotnetRef.invokeMethodAsync('OnEditorContentChanged', editor.getHTML())
                    .catch(err => console.error('[tiptap] OnEditorContentChanged failed', err));
            },
        });

        // Ctrl+K / Ctrl+S shortcuts
        editor.view.dom.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                showLinkPopover(elementId);
            }
        });

        const onCtrlS = (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 's') {
                if (!editor.isFocused) return;
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnSaveShortcut')
                    .catch(err => console.error('[tiptap] OnSaveShortcut failed', err));
            }
        };
        document.addEventListener('keydown', onCtrlS);

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

        // Page link click — open sub-note in dialog via Blazor
        editor.view.dom.addEventListener('click', (e) => {
            const block = e.target.closest('.page-link-block');
            if (!block) return;
            e.preventDefault();
            e.stopPropagation();
            const noteId = block.getAttribute('data-note-id');
            if (noteId) {
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

        _editors[elementId] = { editor, bubbleMenuEl, linkPopover, onCtrlS };
        console.log(`[tiptap] Editor initialized: ${elementId}`);
    },

    focus(elementId) {
        _editors[elementId]?.editor.commands.focus('start');
    },

    setContent(elementId, content) {
        _editors[elementId]?.editor.commands.setContent(content, false);
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

    destroy(elementId) {
        const entry = _editors[elementId];
        if (entry) {
            document.removeEventListener('keydown', entry.onCtrlS);
            entry.editor.destroy();
            entry.bubbleMenuEl.remove();
            entry.linkPopover.popover.remove();
            delete _editors[elementId];
            console.log(`[tiptap] Editor destroyed: ${elementId}`);
        }
    },
};
