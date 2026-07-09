import { editors as _editors } from '../registry.js'

// ── Link popover ────────────────────────────────────────────────────────────

export function createLinkPopover(editorId) {
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

export function showLinkPopover(editorId) {
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
