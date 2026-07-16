(function () {
    'use strict';

    const _active = new Set();
    // Shortcuts that must keep working while a text field is focused (command
    // palette, save). Every other shortcut — bare keys AND modifier combos such
    // as Ctrl+N — is skipped while typing so it can't hijack the input (#214).
    const _inputSafe = new Set(['ctrl+k', 'ctrl+s']);
    let _ref = null;

    function _buildKey(e) {
        // Some synthetic keydown events (e.g. browser credential autofill on the
        // login form) have no `key`, so guard against a falsy value instead of
        // calling toLowerCase() on undefined (#237).
        if (!e.key) return null;

        const mods = [];
        if (e.ctrlKey || e.metaKey) mods.push('ctrl');
        if (e.altKey) mods.push('alt');
        if (e.shiftKey) mods.push('shift');
        mods.push(e.key.toLowerCase());
        return mods.join('+');
    }

    function _onKeyDown(e) {
        const key = _buildKey(e);
        if (!key || !_active.has(key)) return;

        // Skip when a text field has focus, unless the shortcut is explicitly
        // input-safe. This covers modifier combos (e.g. Ctrl+N) too, not just
        // bare keys, so typing in a title/search field is never hijacked (#214).
        if (!_inputSafe.has(key)) {
            const el = document.activeElement;
            const isTextField =
                (el?.tagName === 'INPUT' && el?.type !== 'checkbox') ||
                el?.tagName === 'TEXTAREA' ||
                el?.contentEditable === 'true';
            if (isTextField) return;
        }

        e.preventDefault();
        e.stopPropagation();
        _ref?.invokeMethodAsync('HandleShortcut', key)
            .catch(err => console.error('[shortcuts] HandleShortcut failed:', err));
    }

    window.keyboardShortcuts = {
        init(dotnetRef) {
            _ref = dotnetRef;
            document.addEventListener('keydown', _onKeyDown);
        },
        dispose() {
            document.removeEventListener('keydown', _onKeyDown);
            _ref = null;
            _active.clear();
        },
        register(key) { _active.add(key); },
        unregister(key) { _active.delete(key); },
    };
}());
