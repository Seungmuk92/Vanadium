(function () {
    'use strict';

    const KEY = 'vanadium.recents.v1';
    const MAX = 25; // bounded store; the palette shows fewer (see §5.1 of the spec)

    function _read() {
        try {
            const raw = localStorage.getItem(KEY);
            if (!raw) return [];
            const list = JSON.parse(raw);
            return Array.isArray(list) ? list : [];
        } catch {
            return [];
        }
    }

    function _write(list) {
        try {
            localStorage.setItem(KEY, JSON.stringify(list));
        } catch {
            /* storage full or disabled — Recents is a best-effort convenience */
        }
    }

    window.quickNav = {
        getRecents() {
            return _read();
        },
        pushRecent(entry) {
            if (!entry || !entry.id) return;
            let list = _read().filter(e => e.id !== entry.id); // de-dupe
            list.unshift(entry);                                // most-recent first
            list = list.slice(0, MAX);                          // bound
            _write(list);
        },
        removeRecent(id) {
            if (!id) return;
            _write(_read().filter(e => e.id !== id));
        },
        scrollSelectedIntoView() {
            const el = document.querySelector('.quicknav-results .is-selected');
            if (el) el.scrollIntoView({ block: 'nearest' });
        },
    };
}());
