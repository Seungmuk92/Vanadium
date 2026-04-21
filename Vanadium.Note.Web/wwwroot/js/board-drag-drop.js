window.boardDragDrop = (() => {
    let _dotNetRef = null;
    let _draggingNoteId = null;
    let _draggingFromLabelId = null;
    const _h = {};

    function getColumn(el) {
        return el?.closest('[data-label-id]');
    }

    function getCard(el) {
        return el?.closest('[data-note-id]');
    }

    function clearDropTargets() {
        document.querySelectorAll('.board-column.drop-target')
            .forEach(el => el.classList.remove('drop-target'));
    }

    function setIsDragging(on) {
        const cols = document.querySelector('.board-columns');
        if (cols) cols.classList.toggle('is-dragging', on);
    }

    _h.dragstart = (e) => {
        const card = getCard(e.target);
        if (!card) return;

        _draggingNoteId    = card.dataset.noteId;
        _draggingFromLabelId = card.dataset.labelId;
        console.debug(`[board] Drag started: noteId=${_draggingNoteId}, fromLabel=${_draggingFromLabelId}`);

        // Required: without setData the drag simply won't start in most browsers
        e.dataTransfer.setData('text/plain', _draggingNoteId);
        e.dataTransfer.effectAllowed = 'move';

        // Defer class addition so the ghost image is captured before opacity changes
        requestAnimationFrame(() => {
            card.classList.add('is-dragging');
            setIsDragging(true);
        });
    };

    _h.dragend = (e) => {
        const card = getCard(e.target);
        if (card) card.classList.remove('is-dragging');
        clearDropTargets();
        setIsDragging(false);
        _draggingNoteId = null;
        _draggingFromLabelId = null;
    };

    _h.dragover = (e) => {
        if (!_draggingNoteId) return;
        const col = getColumn(e.target);
        if (col) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
        }
    };

    _h.dragenter = (e) => {
        if (!_draggingNoteId) return;
        const col = getColumn(e.target);
        if (!col) return;
        clearDropTargets();
        if (col.dataset.labelId !== _draggingFromLabelId) {
            col.classList.add('drop-target');
        }
    };

    _h.dragleave = (e) => {
        if (!_draggingNoteId) return;
        const col = getColumn(e.target);
        if (!col) return;
        // Only remove if the pointer actually left the column (not just moved to a child)
        const related = getColumn(e.relatedTarget);
        if (related !== col) col.classList.remove('drop-target');
    };

    _h.drop = (e) => {
        if (!_draggingNoteId) return;
        const col = getColumn(e.target);
        if (!col) return;

        e.preventDefault();
        const toLabelId   = col.dataset.labelId;
        const fromLabelId = _draggingFromLabelId;
        const noteId      = _draggingNoteId;

        clearDropTargets();
        setIsDragging(false);
        _draggingNoteId      = null;
        _draggingFromLabelId = null;

        if (toLabelId !== fromLabelId && _dotNetRef) {
            console.debug(`[board] Drop: noteId=${noteId}, from=${fromLabelId} -> to=${toLabelId}`);
            _dotNetRef.invokeMethodAsync('OnDropFromJs', noteId, fromLabelId, toLabelId)
                .catch(err => console.error('[board] OnDropFromJs failed', err));
        }
    };

    return {
        init(dotNetRef) {
            _dotNetRef = dotNetRef;
            document.addEventListener('dragstart',  _h.dragstart);
            console.log('[board] Drag-drop initialized.');
            document.addEventListener('dragend',    _h.dragend);
            document.addEventListener('dragover',   _h.dragover);
            document.addEventListener('dragenter',  _h.dragenter);
            document.addEventListener('dragleave',  _h.dragleave);
            document.addEventListener('drop',       _h.drop);
        },
        dispose() {
            document.removeEventListener('dragstart',  _h.dragstart);
            document.removeEventListener('dragend',    _h.dragend);
            document.removeEventListener('dragover',   _h.dragover);
            document.removeEventListener('dragenter',  _h.dragenter);
            document.removeEventListener('dragleave',  _h.dragleave);
            document.removeEventListener('drop',       _h.drop);
            _dotNetRef = null;
            console.log('[board] Drag-drop disposed.');
        }
    };
})();
