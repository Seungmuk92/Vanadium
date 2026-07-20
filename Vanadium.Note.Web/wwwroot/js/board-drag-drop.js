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

    // ── Touch drag ───────────────────────────────────────────────────────────
    // HTML5 drag events never fire from touch input, so touch devices get a
    // parallel long-press drag path that reuses the same OnDropFromJs handler.
    // A quick tap still falls through to the card's Blazor @onclick (open note);
    // only a deliberate long-press turns the card into a draggable ghost.
    const LONG_PRESS_MS = 220;   // hold time before a card becomes draggable
    const MOVE_SLOP_PX  = 10;    // pre-drag finger travel that reads as a scroll, not a drag
    const EDGE_ZONE_PX  = 48;    // distance from the container edge that triggers auto-scroll
    const EDGE_SPEED_PX = 12;    // horizontal auto-scroll step per animation frame

    const _t = {
        card: null, noteId: null, fromLabelId: null,
        startX: 0, startY: 0, lastX: 0, lastY: 0,
        offsetX: 0, offsetY: 0,
        pressTimer: null, dragging: false,
        ghost: null, currentCol: null,
        scrollDir: 0, scrollRAF: null,
    };

    function stopAutoScroll() {
        if (_t.scrollRAF) { cancelAnimationFrame(_t.scrollRAF); _t.scrollRAF = null; }
        _t.scrollDir = 0;
    }

    function resetTouch() {
        if (_t.pressTimer) { clearTimeout(_t.pressTimer); _t.pressTimer = null; }
        stopAutoScroll();
        if (_t.ghost) { _t.ghost.remove(); _t.ghost = null; }
        if (_t.card) _t.card.classList.remove('is-dragging');
        clearDropTargets();
        setIsDragging(false);
        _t.card = _t.noteId = _t.fromLabelId = null;
        _t.currentCol = null;
        _t.dragging = false;
    }

    function beginTouchDrag() {
        if (!_t.card) return;
        _t.dragging = true;
        _t.card.classList.add('is-dragging');
        setIsDragging(true);

        // Floating clone that trails the finger. pointer-events:none is essential
        // so document.elementFromPoint resolves the column underneath, not the ghost.
        const rect = _t.card.getBoundingClientRect();
        const ghost = _t.card.cloneNode(true);
        ghost.classList.add('board-card-touch-ghost');
        Object.assign(ghost.style, {
            position: 'fixed',
            left: `${rect.left}px`,
            top: `${rect.top}px`,
            width: `${rect.width}px`,
            margin: '0',
            pointerEvents: 'none',
            zIndex: '10000',
            opacity: '0.92',
            transform: 'scale(1.03)',
            boxShadow: '0 8px 24px rgba(0, 0, 0, 0.3)',
        });
        document.body.appendChild(ghost);
        _t.ghost = ghost;
        _t.offsetX = _t.startX - rect.left;
        _t.offsetY = _t.startY - rect.top;
    }

    function highlightColumnUnderFinger() {
        const el = document.elementFromPoint(_t.lastX, _t.lastY);
        const col = getColumn(el);
        clearDropTargets();
        if (col && col.dataset.labelId !== _t.fromLabelId) col.classList.add('drop-target');
        _t.currentCol = col;
    }

    function updateAutoScroll(clientX) {
        const container = document.querySelector('.board-columns');
        if (!container) { stopAutoScroll(); return; }
        const rect = container.getBoundingClientRect();
        let dir = 0;
        if (clientX < rect.left + EDGE_ZONE_PX) dir = -1;
        else if (clientX > rect.right - EDGE_ZONE_PX) dir = 1;
        _t.scrollDir = dir;

        if (dir !== 0 && !_t.scrollRAF) {
            const step = () => {
                if (_t.scrollDir === 0 || !_t.dragging) { _t.scrollRAF = null; return; }
                container.scrollLeft += _t.scrollDir * EDGE_SPEED_PX;
                // Columns slide under a stationary finger, so refresh the target.
                highlightColumnUnderFinger();
                _t.scrollRAF = requestAnimationFrame(step);
            };
            _t.scrollRAF = requestAnimationFrame(step);
        }
    }

    _h.touchstart = (e) => {
        if (e.touches.length !== 1) return;   // ignore multi-touch (pinch/zoom)
        const card = getCard(e.target);
        if (!card) return;
        const touch = e.touches[0];
        _t.card = card;
        _t.noteId = card.dataset.noteId;
        _t.fromLabelId = card.dataset.labelId;
        _t.startX = _t.lastX = touch.clientX;
        _t.startY = _t.lastY = touch.clientY;
        _t.dragging = false;
        _t.pressTimer = setTimeout(() => {
            _t.pressTimer = null;
            beginTouchDrag();
        }, LONG_PRESS_MS);
    };

    _h.touchmove = (e) => {
        if (!_t.card) return;
        const touch = e.touches[0];

        if (!_t.dragging) {
            // Long-press still pending: enough travel means the user is scrolling,
            // so abandon the drag candidate and let the native scroll proceed.
            const dx = Math.abs(touch.clientX - _t.startX);
            const dy = Math.abs(touch.clientY - _t.startY);
            if (dx > MOVE_SLOP_PX || dy > MOVE_SLOP_PX) {
                if (_t.pressTimer) { clearTimeout(_t.pressTimer); _t.pressTimer = null; }
                _t.card = _t.noteId = _t.fromLabelId = null;
            }
            return;
        }

        // Dragging: keep the page/list from scrolling out from under the finger.
        e.preventDefault();
        _t.lastX = touch.clientX;
        _t.lastY = touch.clientY;
        if (_t.ghost) {
            _t.ghost.style.left = `${touch.clientX - _t.offsetX}px`;
            _t.ghost.style.top  = `${touch.clientY - _t.offsetY}px`;
        }
        highlightColumnUnderFinger();
        updateAutoScroll(touch.clientX);
    };

    _h.touchend = (e) => {
        if (!_t.card) { resetTouch(); return; }
        if (!_t.dragging) {
            // Quick tap: leave the emulated click alone so @onclick opens the note.
            resetTouch();
            return;
        }
        // A real drag: suppress the emulated click so the card doesn't also open.
        e.preventDefault();
        const noteId      = _t.noteId;
        const fromLabelId = _t.fromLabelId;
        const toLabelId   = _t.currentCol?.dataset.labelId;
        resetTouch();

        if (toLabelId && toLabelId !== fromLabelId && _dotNetRef) {
            console.debug(`[board] Touch drop: noteId=${noteId}, from=${fromLabelId} -> to=${toLabelId}`);
            _dotNetRef.invokeMethodAsync('OnDropFromJs', noteId, fromLabelId, toLabelId)
                .catch(err => console.error('[board] OnDropFromJs (touch) failed', err));
        }
    };

    _h.touchcancel = () => resetTouch();

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
            document.addEventListener('touchstart', _h.touchstart, { passive: true });
            document.addEventListener('touchmove',  _h.touchmove,  { passive: false });
            document.addEventListener('touchend',   _h.touchend);
            document.addEventListener('touchcancel', _h.touchcancel);
        },
        dispose() {
            document.removeEventListener('dragstart',  _h.dragstart);
            document.removeEventListener('dragend',    _h.dragend);
            document.removeEventListener('dragover',   _h.dragover);
            document.removeEventListener('dragenter',  _h.dragenter);
            document.removeEventListener('dragleave',  _h.dragleave);
            document.removeEventListener('drop',       _h.drop);
            document.removeEventListener('touchstart', _h.touchstart, { passive: true });
            document.removeEventListener('touchmove',  _h.touchmove,  { passive: false });
            document.removeEventListener('touchend',   _h.touchend);
            document.removeEventListener('touchcancel', _h.touchcancel);
            resetTouch();
            _dotNetRef = null;
            console.debug('[board] Drag-drop disposed.');
        }
    };
})();
