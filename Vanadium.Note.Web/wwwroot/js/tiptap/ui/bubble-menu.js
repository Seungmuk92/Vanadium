import { editors as _editors } from '../registry.js'
import { showLinkPopover } from './link-popover.js'

// ── Bubble menu ─────────────────────────────────────────────────────────────

export function createBubbleMenu(editorId) {
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
            // preventDefault keeps the editor selection from collapsing when the
            // button takes focus. Bind touchstart too so taps fire immediately and
            // reliably on mobile; preventDefault there also suppresses the emulated
            // mousedown, so the handler never runs twice for one tap.
            const activate = (e) => {
                e.preventDefault();
                if (item.cmd === 'link') {
                    showLinkPopover(editorId);
                } else {
                    runCommand(_editors[editorId]?.editor, item.cmd);
                }
            };
            btn.addEventListener('mousedown', activate);
            btn.addEventListener('touchstart', activate, { passive: false });
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
