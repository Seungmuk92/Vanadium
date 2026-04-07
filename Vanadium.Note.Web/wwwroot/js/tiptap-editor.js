import { Editor } from 'https://esm.sh/@tiptap/core@2'
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2'
import BubbleMenu from 'https://esm.sh/@tiptap/extension-bubble-menu@2'
import Placeholder from 'https://esm.sh/@tiptap/extension-placeholder@2'
import Link from 'https://esm.sh/@tiptap/extension-link@2'
import Image from 'https://esm.sh/@tiptap/extension-image@2'

const _editors = {};

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
    init(elementId, dotnetRef, initialContent) {
        const el = document.getElementById(elementId);
        if (!el) return;

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
                    allowBase64: true,
                    HTMLAttributes: { class: 'tiptap-image' },
                }),
                BubbleMenu.configure({
                    element: bubbleMenuEl,
                    shouldShow: ({ from, to }) => from !== to,
                }),
            ],
            content: initialContent || '',
            onUpdate({ editor }) {
                dotnetRef.invokeMethodAsync('OnEditorContentChanged', editor.getHTML());
            },
        });

        // Ctrl+K shortcut
        editor.view.dom.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                showLinkPopover(elementId);
            }
        });

        // Clipboard image paste
        editor.view.dom.addEventListener('paste', (e) => {
            const items = e.clipboardData?.items;
            if (!items) return;
            for (const item of items) {
                if (item.type.startsWith('image/')) {
                    e.preventDefault();
                    const file = item.getAsFile();
                    if (!file) return;
                    const reader = new FileReader();
                    reader.onload = (ev) => {
                        const src = ev.target.result;
                        editor.chain().focus().setImage({ src }).run();
                    };
                    reader.readAsDataURL(file);
                    break;
                }
            }
        });

        _editors[elementId] = { editor, bubbleMenuEl, linkPopover };
    },

    focus(elementId) {
        _editors[elementId]?.editor.commands.focus('start');
    },

    setContent(elementId, content) {
        _editors[elementId]?.editor.commands.setContent(content, false);
    },

    destroy(elementId) {
        const entry = _editors[elementId];
        if (entry) {
            entry.editor.destroy();
            entry.bubbleMenuEl.remove();
            entry.linkPopover.popover.remove();
            delete _editors[elementId];
        }
    },
};
