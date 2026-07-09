// Keyboard focus trap for custom (non-MudBlazor) dialogs.
// Keeps Tab / Shift+Tab focus cycling within a container element while it is
// active, and restores focus to the previously focused element on release.
// Only one trap is active at a time (the app never stacks these dialogs).
window.focusTrap = (function () {
    let activeElement = null;
    let previouslyFocused = null;

    const FOCUSABLE_SELECTOR = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    function getFocusable(container) {
        return Array.from(container.querySelectorAll(FOCUSABLE_SELECTOR))
            .filter(el => el.offsetParent !== null || el.getClientRects().length > 0);
    }

    function onKeyDown(e) {
        if (e.key !== 'Tab' || activeElement === null) return;

        const focusable = getFocusable(activeElement);
        if (focusable.length === 0) {
            // Nothing focusable inside — keep focus pinned to the container.
            e.preventDefault();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const current = document.activeElement;

        if (e.shiftKey) {
            if (current === first || !activeElement.contains(current)) {
                e.preventDefault();
                last.focus();
            }
        } else {
            if (current === last || !activeElement.contains(current)) {
                e.preventDefault();
                first.focus();
            }
        }
    }

    return {
        activate: function (element) {
            if (element === null || element === activeElement) return;
            previouslyFocused = document.activeElement;
            activeElement = element;
            document.addEventListener('keydown', onKeyDown, true);
        },
        deactivate: function () {
            document.removeEventListener('keydown', onKeyDown, true);
            activeElement = null;
            if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
                try { previouslyFocused.focus(); } catch { /* element may be gone */ }
            }
            previouslyFocused = null;
        }
    };
})();
