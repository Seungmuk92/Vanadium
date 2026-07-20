(function () {
    'use strict';

    let dotNetRef = null;

    function _notify() {
        if (!dotNetRef) return;
        try {
            // invokeMethodAsync returns a promise, so a disposed .NET reference rejects
            // ASYNCHRONOUSLY and would escape this try/catch as an unhandled rejection —
            // the .catch is what actually swallows it. try/catch covers a synchronous
            // throw from a reference that is already torn down.
            dotNetRef.invokeMethodAsync('SetOnline', navigator.onLine).catch(() => { });
        } catch {
            /* the .NET reference went away mid-flight — nothing left to notify */
        }
    }

    window.networkStatus = {
        isOnline() {
            return navigator.onLine;
        },
        init(ref) {
            // Re-initialising must not stack duplicate listeners.
            window.networkStatus.dispose();
            dotNetRef = ref;
            window.addEventListener('online', _notify);
            window.addEventListener('offline', _notify);
            return navigator.onLine;
        },
        dispose() {
            window.removeEventListener('online', _notify);
            window.removeEventListener('offline', _notify);
            dotNetRef = null;
        },
    };
}());
