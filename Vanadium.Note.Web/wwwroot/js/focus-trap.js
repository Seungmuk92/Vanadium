// Keyboard focus trap for custom (non-MudBlazor) dialogs.
// Keeps Tab / Shift+Tab focus cycling within a container element while it is
// active, and restores focus to the previously focused element on release.
//
// Traps nest: the quick-navigation palette (Ctrl+K) can open on top of an
// already-trapped dialog (e.g. Board's add-note dialog), so activate/deactivate
// maintain a stack. Only the top entry traps Tab; releasing it restores both
// focus and the trap underneath, instead of leaving the lower dialog untrapped.
// Release is LIFO — deactivate() always releases the most recent trap, which
// holds because a nested dialog covers the one below it.
window.focusTrap = (function () {
    // [{ element, previouslyFocused }] — last entry is the active trap.
    const stack = [];

    function currentTrap() {
        return stack.length > 0 ? stack[stack.length - 1] : null;
    }

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
        const active = currentTrap();
        if (e.key !== 'Tab' || active === null) return;

        const activeElement = active.element;
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
            if (element === null) return;
            const active = currentTrap();
            if (active !== null && active.element === element) return;
            stack.push({ element: element, previouslyFocused: document.activeElement });
            // One shared listener serves the whole stack; only attach it once.
            if (stack.length === 1) document.addEventListener('keydown', onKeyDown, true);
        },
        deactivate: function () {
            const released = stack.pop();
            if (released === undefined) return;
            if (stack.length === 0) document.removeEventListener('keydown', onKeyDown, true);

            const previouslyFocused = released.previouslyFocused;
            if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
                try { previouslyFocused.focus(); } catch { /* element may be gone */ }
            }
        }
    };
})();
