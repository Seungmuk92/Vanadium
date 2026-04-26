(function () {
    'use strict';

    const _active = new Set();
    let _ref = null;

    function _buildKey(e) {
        const mods = [];
        if (e.ctrlKey || e.metaKey) mods.push('ctrl');
        if (e.altKey) mods.push('alt');
        if (e.shiftKey) mods.push('shift');
        mods.push(e.key.toLowerCase());
        return mods.join('+');
    }

    function _onKeyDown(e) {
        const key = _buildKey(e);
        if (!_active.has(key)) return;

        // For bare keys (no Ctrl/Alt/Meta), skip when a text field has focus
        const hasModifier = e.ctrlKey || e.metaKey || e.altKey;
        if (!hasModifier) {
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
