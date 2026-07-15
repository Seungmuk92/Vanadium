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

    // Reference-counted: NoteEditor and SubNoteDialog can be armed at the same
    // time (a page-link/mention click opens the dialog on top of an editor that
    // still holds unsaved changes). A single boolean would let the dialog's
    // disable() strip the listener the editor still needs, so track how many
    // callers want the guard and only remove the listener when the last one
    // disarms (issue #192).
    let _armedCount = 0;

    window.unsavedChangesInterop = {
        enable() {
            _armedCount++;
            if (_armedCount === 1)
                window.addEventListener('beforeunload', _handler);
        },
        disable() {
            if (_armedCount === 0) return;
            _armedCount--;
            if (_armedCount === 0)
                window.removeEventListener('beforeunload', _handler);
        }
    };
})();
