(function () {
    'use strict';

    // Guards against silently losing unsaved edits when the browser tab is
    // closed or reloaded during the auto-save debounce window. NoteEditor arms
    // this whenever it holds pending (not-yet-persisted) changes and disarms it
    // once those changes are saved (issue #109).
    function _handler(e) {
        // preventDefault + a returnValue value are what trigger the browser's
        // native "Leave site? Changes you made may not be saved." dialog.
        e.preventDefault();
        e.returnValue = '';
        return '';
    }

    let _armed = false;

    window.unsavedChangesInterop = {
        enable() {
            if (_armed) return;
            _armed = true;
            window.addEventListener('beforeunload', _handler);
        },
        disable() {
            if (!_armed) return;
            _armed = false;
            window.removeEventListener('beforeunload', _handler);
        }
    };
})();
