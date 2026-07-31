window.boardDragDrop = (() => {
    let _dotNetRef = null;
    let _draggingNoteId = null;
    let _draggingFromOptionId = null;
    const _h = {};

    function getColumn(el) {
        return el?.closest('[data-option-id]');
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

        _draggingNoteId = card.dataset.noteId;
        // Cards in the "No value" column carry no data-option-id: the note has no value for the
        // grouping property yet (#375). Normalize the missing attribute to null so it stays a
        // valid drag source — the drop still resolves to a single set-value write on the target.
        _draggingFromOptionId = card.dataset.optionId ?? null;
        console.debug(`[board] Drag started: noteId=${_draggingNoteId}, fromOption=${_draggingFromOptionId}`);

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
        _draggingFromOptionId = null;
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
        if (col.dataset.optionId !== _draggingFromOptionId) {
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
        const toOptionId = col.dataset.optionId;
        const fromOptionId = _draggingFromOptionId;
        const noteId = _draggingNoteId;

        clearDropTargets();
        setIsDragging(false);
        _draggingNoteId = null;
        _draggingFromOptionId = null;

        if (toOptionId !== fromOptionId && _dotNetRef) {
            console.debug(`[board] Drop: noteId=${noteId}, from=${fromOptionId} -> to=${toOptionId}`);
            _dotNetRef.invokeMethodAsync('OnDropFromJs', noteId, fromOptionId, toOptionId)
                .catch(err => console.error('[board] OnDropFromJs failed', err));
        }
    };

    return {
        init(dotNetRef) {
            _dotNetRef = dotNetRef;
            document.addEventListener('dragstart',  _h.dragstart);
            console.debug('[board] Drag-drop initialized.');
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
            console.debug('[board] Drag-drop disposed.');
        }
    };
})();
