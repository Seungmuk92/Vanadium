import { Node } from 'https://esm.sh/@tiptap/core@2'
import { renderMermaidSvg } from '../mermaid.js'

// ── Mermaid node ─────────────────────────────────────────────────────────────

export const MermaidNode = Node.create({
    name: 'mermaid',
    group: 'block',
    atom: true,

    addAttributes() {
        return {
            code: { default: '' },
        };
    },

    parseHTML() {
        return [{
            tag: 'pre[data-type="mermaid"]',
            getAttrs: (el) => ({
                code: el.querySelector('code')?.textContent ?? '',
            }),
        }];
    },

    renderHTML({ node }) {
        return [
            'pre',
            { 'data-type': 'mermaid' },
            ['code', {}, node.attrs.code],
        ];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            // ── DOM skeleton ──────────────────────────────────────────────
            const wrapper = document.createElement('div');
            wrapper.className = 'mermaid-block';

            const preview = document.createElement('div');
            preview.className = 'mermaid-preview';

            const editArea = document.createElement('div');
            editArea.className = 'mermaid-edit';
            editArea.style.display = 'none';

            const textarea = document.createElement('textarea');
            textarea.className = 'mermaid-textarea';
            textarea.placeholder = 'Enter Mermaid diagram code…\n\nExample:\ngraph TD\n  A-->B';
            textarea.spellcheck = false;

            const editActions = document.createElement('div');
            editActions.className = 'mermaid-edit-actions';

            const applyBtn = document.createElement('button');
            applyBtn.textContent = 'Apply';
            applyBtn.className = 'mermaid-apply-btn';

            const hint = document.createElement('span');
            hint.className = 'mermaid-hint';
            hint.textContent = 'Esc to cancel · Tab inserts spaces';

            editActions.appendChild(applyBtn);
            editActions.appendChild(hint);
            editArea.appendChild(textarea);
            editArea.appendChild(editActions);

            wrapper.appendChild(preview);
            wrapper.appendChild(editArea);

            let currentCode = node.attrs.code;
            let isEditing = false;

            // ── Render preview ────────────────────────────────────────────
            async function renderPreview(code) {
                if (!code.trim()) {
                    preview.innerHTML = '<span class="mermaid-placeholder">▦ Mermaid diagram · click to edit</span>';
                    return;
                }
                preview.innerHTML = '<span class="mermaid-placeholder mermaid-loading">Rendering…</span>';
                try {
                    preview.innerHTML = await renderMermaidSvg(code);
                } catch (err) {
                    // Mermaid error messages can echo user input; insert as text.
                    const errDiv = document.createElement('div');
                    errDiv.className = 'mermaid-error';
                    errDiv.textContent = `⚠ ${err.message || 'Syntax error'}`;
                    preview.replaceChildren(errDiv);
                }
            }

            // ── Edit / preview toggle ─────────────────────────────────────
            function enterEdit() {
                if (isEditing) return;
                isEditing = true;
                textarea.value = currentCode;
                preview.style.display = 'none';
                editArea.style.display = '';
                // Rows auto-grow: at least 6, at most 24 lines
                textarea.rows = Math.min(24, Math.max(6, (currentCode.match(/\n/g) || []).length + 3));
                setTimeout(() => { textarea.focus(); textarea.setSelectionRange(textarea.value.length, textarea.value.length); }, 0);
            }

            function commitEdit() {
                if (!isEditing) return;
                isEditing = false;
                const newCode = textarea.value;
                preview.style.display = '';
                editArea.style.display = 'none';
                if (newCode !== currentCode) {
                    currentCode = newCode;
                    const pos = typeof getPos === 'function' ? getPos() : null;
                    if (pos !== null) {
                        editor.view.dispatch(
                            editor.state.tr.setNodeMarkup(pos, null, { ...node.attrs, code: newCode })
                        );
                    }
                }
                renderPreview(currentCode);
            }

            function cancelEdit() {
                if (!isEditing) return;
                isEditing = false;
                preview.style.display = '';
                editArea.style.display = 'none';
                textarea.value = currentCode;
            }

            // ── Event wiring ──────────────────────────────────────────────
            applyBtn.addEventListener('mousedown', (e) => {
                e.preventDefault();
                commitEdit();
            });

            textarea.addEventListener('keydown', (e) => {
                if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); return; }
                if (e.key === 'Tab') {
                    e.preventDefault();
                    const s = textarea.selectionStart;
                    const end = textarea.selectionEnd;
                    textarea.value = textarea.value.substring(0, s) + '    ' + textarea.value.substring(end);
                    textarea.selectionStart = textarea.selectionEnd = s + 4;
                }
            });

            // Auto-grow textarea rows on input
            textarea.addEventListener('input', () => {
                textarea.rows = Math.min(24, Math.max(6, (textarea.value.match(/\n/g) || []).length + 3));
            });

            preview.addEventListener('click', () => {
                if (editor.isEditable) enterEdit();
            });

            // Initial render; auto-enter edit mode if empty
            renderPreview(currentCode);
            if (!currentCode.trim()) {
                setTimeout(() => enterEdit(), 80);
            }

            return {
                dom: wrapper,

                update(updatedNode) {
                    if (updatedNode.type.name !== 'mermaid') return false;
                    if (updatedNode.attrs.code !== currentCode && !isEditing) {
                        currentCode = updatedNode.attrs.code;
                        renderPreview(currentCode);
                    }
                    return true;
                },

                // Swallow all events inside the edit area so ProseMirror doesn't steal them
                stopEvent(event) {
                    return editArea.contains(event.target);
                },

                // Prevent ProseMirror from reacting to DOM mutations inside this view
                ignoreMutation() { return true; },

                destroy() { /* nothing to clean up */ },
            };
        };
    },
});
