(function () {
    'use strict';

    let _ref = null;

    // The `storage` event fires only in OTHER tabs/windows of the same origin,
    // never in the tab that made the change. So this exclusively carries
    // cross-tab auth token changes (login/logout), which is what we want to
    // sync back into TokenStore's cache (issue #134).
    function _onStorage(e) {
        // e.key is null when localStorage.clear() is called; treat that as an
        // authToken change too. Otherwise only react to the authToken key.
        if (e.key !== null && e.key !== 'authToken') return;
        const newValue = e.key === null ? null : e.newValue;
        _ref?.invokeMethodAsync('OnExternalTokenChange', newValue)
            .catch(err => console.error('[token-sync] OnExternalTokenChange failed:', err));
    }

    window.tokenSync = {
        init(dotnetRef) {
            _ref = dotnetRef;
            // Idempotent: removing first keeps a re-init (e.g. after logout then
            // login re-creates the layout) from stacking duplicate listeners.
            window.removeEventListener('storage', _onStorage);
            window.addEventListener('storage', _onStorage);
        },
        dispose() {
            window.removeEventListener('storage', _onStorage);
            _ref = null;
        }
    };
}());
