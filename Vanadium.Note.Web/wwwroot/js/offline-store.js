(function () {
    'use strict';

    // Enumeration helper for the per-note localStorage keys used by the offline save
    // queue and its note claims (#211). The direct `localStorage.getItem/setItem/
    // removeItem` interop calls cover every single-key operation; only listing keys by
    // prefix needs JS, and keeping storage per-key is what makes each queue write atomic.
    window.offlineStore = {
        valuesWithPrefix(prefix) {
            const values = [];
            for (let i = 0; i < localStorage.length; i++) {
                const key = localStorage.key(i);
                if (key && key.startsWith(prefix)) {
                    const value = localStorage.getItem(key);
                    if (value) values.push(value);
                }
            }
            return values;
        },
    };
}());
