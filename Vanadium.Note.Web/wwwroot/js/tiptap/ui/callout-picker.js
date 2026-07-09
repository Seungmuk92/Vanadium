// ── Callout emoji picker ─────────────────────────────────────────────────────

const CALLOUT_EMOJIS = ['💡', 'ℹ️', '⚠️', '🚨', '✅', '📝', '💬', '🔥', '📌', '🎯'];

let _calloutPicker = null;

export function hideCalloutPicker() {
    _calloutPicker?.remove();
    _calloutPicker = null;
}

export function showCalloutEmojiPicker(emojiEl, editor) {
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
