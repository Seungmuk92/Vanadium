import { Editor } from 'https://esm.sh/@tiptap/core@2'
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2'
import BubbleMenu from 'https://esm.sh/@tiptap/extension-bubble-menu@2'
import Placeholder from 'https://esm.sh/@tiptap/extension-placeholder@2'
import Link from 'https://esm.sh/@tiptap/extension-link@2'
import Image from 'https://esm.sh/@tiptap/extension-image@2'
import { Markdown } from 'https://esm.sh/tiptap-markdown@0.9.0?deps=@tiptap/core@2'
import TaskList from 'https://esm.sh/@tiptap/extension-task-list@2'
import TaskItem from 'https://esm.sh/@tiptap/extension-task-item@2'
import Table from 'https://esm.sh/@tiptap/extension-table@2'
import TableRow from 'https://esm.sh/@tiptap/extension-table-row@2'
import TableHeader from 'https://esm.sh/@tiptap/extension-table-header@2'
import TableCell from 'https://esm.sh/@tiptap/extension-table-cell@2'

import { editors as _editors } from './registry.js'
import { getMermaid, renderMermaidSvg } from './mermaid.js'
import { CodeBlock, lowlight } from './extensions/code-block.js'
import { TabIndent } from './extensions/tab-indent.js'
import { createSlashCommandsExtension } from './extensions/slash-commands.js'
import { createMentionExtension } from './extensions/mention.js'
import { ToggleKeymap } from './extensions/toggle-keymap.js'
import { FileAttachment } from './nodes/file-attachment.js'
import { PageLink } from './nodes/page-link.js'
import { NoteMention } from './nodes/note-mention.js'
import { Callout } from './nodes/callout.js'
import { MermaidNode } from './nodes/mermaid-node.js'
import { Toggle, ToggleSummary, ToggleContent } from './nodes/toggle.js'
import { CollapsibleHeading } from './nodes/collapsible-heading.js'
import { AccordionGroup } from './nodes/accordion.js'
import { createBubbleMenu } from './ui/bubble-menu.js'
import { createLinkPopover, showLinkPopover } from './ui/link-popover.js'
import { showCalloutEmojiPicker, hideCalloutPicker } from './ui/callout-picker.js'
import { uploadWithProgress, createProgressToast, validateUpload, showUploadNotice } from './upload.js'
import { resolveAuthenticatedImages } from './images.js'

// ── Public API ───────────────────────────────────────────────────────────────

window.tiptapInterop = {
    async init(elementId, dotnetRef, initialContent, apiBaseUrl, editable = true) {
        const el = document.getElementById(elementId);
        if (!el) {
            console.error(`[tiptap] Element not found: '${elementId}'`);
            return;
        }

        // Resolve the current auth token on demand rather than capturing it once,
        // so image load/upload after a JWT expiry + re-login uses the fresh token
        // (issue #126). Blazor's TokenStore returns the latest token for the session.
        const getAuthToken = () => dotnetRef.invokeMethodAsync('GetAuthTokenAsync');

        const bubbleMenuEl = createBubbleMenu(elementId);
        const linkPopover  = createLinkPopover(elementId);

        const editor = new Editor({
            element: el,
            extensions: [
                StarterKit.configure({ codeBlock: false, heading: false }),
                CollapsibleHeading.configure({ levels: [1, 2, 3, 4, 5, 6] }),
                CodeBlock.configure({ lowlight, defaultLanguage: 'plaintext' }),
                Markdown.configure({
                    html: true,
                    tightLists: true,
                    bulletListMarker: '-',
                    linkify: false,
                    breaks: false,
                    transformPastedText: true,
                    transformCopiedText: false,
                }),
                Placeholder.configure({
                    // includeChildren + showOnlyCurrent:false so empty toggle
                    // summaries always show their hint; CSS only renders the
                    // ::before for the first paragraph and toggle summaries,
                    // so other empty nodes are visually unchanged.
                    includeChildren: true,
                    showOnlyCurrent: false,
                    placeholder: ({ node }) =>
                        node.type.name === 'toggleSummary' ? 'Toggle summary' : "Write something, or type '/' for commands",
                }),
                Link.configure({
                    autolink: true,
                    openOnClick: false,
                    HTMLAttributes: { target: '_blank', rel: 'noopener noreferrer' },
                }),
                Image.configure({
                    inline: false,
                    HTMLAttributes: { class: 'tiptap-image' },
                }),
                TaskList,
                TaskItem.configure({ nested: true }),
                Table.configure({ resizable: false }),
                TableRow,
                TableHeader,
                TableCell,
                TabIndent,
                FileAttachment,
                PageLink,
                NoteMention,
                Callout,
                MermaidNode,
                Toggle,
                ToggleSummary,
                ToggleContent,
                AccordionGroup,
                ToggleKeymap,
                createSlashCommandsExtension(dotnetRef),
                createMentionExtension(dotnetRef),
                BubbleMenu.configure({
                    element: bubbleMenuEl,
                    shouldShow: ({ editor, from, to }) => editor.isFocused && from !== to,
                }),
            ],
            content: initialContent || '',
            editable,
            onUpdate({ editor }) {
                dotnetRef.invokeMethodAsync('OnEditorContentChanged', editor.getHTML())
                    .catch(err => console.error('[tiptap] OnEditorContentChanged failed', err));
            },
        });

        // Ctrl+K shortcut
        editor.view.dom.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                // Stop propagation so the event does not bubble to the global
                // ctrl+k handler (Quick Navigation); otherwise both the link
                // popover and the Quick Nav dialog would open at once.
                e.stopPropagation();
                showLinkPopover(elementId);
            }
        });

        // Clipboard image paste — upload to server, insert URL
        editor.view.dom.addEventListener('paste', async (e) => {
            const items = e.clipboardData?.items;
            if (!items) return;
            for (const item of items) {
                if (item.type.startsWith('image/')) {
                    e.preventDefault();
                    const file = item.getAsFile();
                    if (!file) return;

                    // Guard before uploading — reject unsupported/oversized images
                    // client-side so the paste fails fast (server stays authoritative).
                    const notice = validateUpload(file, 'image');
                    if (notice) {
                        showUploadNotice(notice);
                        console.warn('[tiptap] Paste image rejected by client guard', { file: file.name, type: file.type, size: file.size });
                        break;
                    }

                    const formData = new FormData();
                    formData.append('file', file);
                    const authToken = await getAuthToken();
                    const headers = authToken ? { 'Authorization': `Bearer ${authToken}` } : {};
                    const toast = createProgressToast(file.name || 'image');

                    try {
                        const { url } = await uploadWithProgress(
                            `${apiBaseUrl}/api/images`, formData, headers,
                            (ratio) => toast.update(ratio)
                        );
                        toast.done();
                        console.debug(`[tiptap] Image pasted and uploaded: ${file.name} (${file.size}B)`);
                        editor.chain().focus().setImage({ src: `${apiBaseUrl}${url}` }).run();
                        // The browser cannot load /api/images/* without a JWT, so resolve
                        // the newly inserted <img> to a Blob URL immediately after insertion.
                        const entry = _editors[elementId];
                        if (entry) {
                            await resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, getAuthToken, entry.blobUrls, entry.blobUrlCache);
                        }
                    } catch (err) {
                        toast.error();
                        console.error('[tiptap] Paste image upload failed', { file: file.name, size: file.size, error: err.message });
                    }
                    break;
                }
            }
        });

        // File attachment click — ProseMirror intercepts clicks, so we handle it manually
        editor.view.dom.addEventListener('click', (e) => {
            const link = e.target.closest('a.file-attachment');
            if (!link) return;
            e.preventDefault();
            e.stopPropagation();
            const a = document.createElement('a');
            a.href = link.getAttribute('href');
            a.download = link.getAttribute('data-filename') || '';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        });

        // Plain link click — the Link extension runs with openOnClick:false so that
        // clicking a link's text in edit mode positions the caret instead of
        // navigating, but that leaves ProseMirror swallowing the click entirely so
        // the link never opens (issue #340). Open it manually: in read-only mode a
        // plain click navigates; in edit mode only Ctrl/Cmd+click navigates, so a
        // plain click keeps placing the caret for editing (behavior unchanged).
        // File attachments have their own handler above and are excluded here.
        editor.view.dom.addEventListener('click', (e) => {
            const link = e.target.closest('a[href]');
            if (!link || link.classList.contains('file-attachment')) return;
            if (editor.isEditable && !(e.ctrlKey || e.metaKey)) return;
            const href = link.getAttribute('href');
            if (!href) return;
            e.preventDefault();
            e.stopPropagation();
            // 'noopener,noreferrer' mirrors the rendered <a rel="noopener noreferrer">
            // so the opened tab cannot reach back through window.opener.
            window.open(href, '_blank', 'noopener,noreferrer');
        });

        // Callout emoji click — prevent cursor move, open emoji picker
        editor.view.dom.addEventListener('mousedown', (e) => {
            if (e.target.classList.contains('callout-emoji')) e.preventDefault();
        });

        editor.view.dom.addEventListener('click', (e) => {
            const emojiEl = e.target.closest('.callout-emoji');
            if (!emojiEl) return;
            showCalloutEmojiPicker(emojiEl, editor);
        });

        // Page link click — open sub-note in dialog via Blazor
        editor.view.dom.addEventListener('click', (e) => {
            const block = e.target.closest('.page-link-block');
            if (!block) return;
            e.preventDefault();
            e.stopPropagation();
            const noteId = block.getAttribute('data-note-id');
            if (noteId) {
                // Blur the editor before the async dialog opens so the bubble menu hides immediately
                editor.commands.blur();
                dotnetRef.invokeMethodAsync('OnPageLinkClick', noteId)
                    .catch(err => console.error('[tiptap] OnPageLinkClick failed', err));
            }
        });

        // File drag & drop
        editor.view.dom.addEventListener('dragover', (e) => {
            if (e.dataTransfer?.types.includes('Files')) {
                e.preventDefault();
                editor.view.dom.classList.add('drag-over');
            }
        });

        editor.view.dom.addEventListener('dragleave', (e) => {
            if (!editor.view.dom.contains(e.relatedTarget)) {
                editor.view.dom.classList.remove('drag-over');
            }
        });

        editor.view.dom.addEventListener('drop', async (e) => {
            editor.view.dom.classList.remove('drag-over');
            const files = Array.from(e.dataTransfer?.files || []);
            if (files.length === 0) return;
            e.preventDefault();

            const pos = editor.view.posAtCoords({ left: e.clientX, top: e.clientY });
            const insertPos = pos ? pos.pos : editor.state.doc.content.size;

            const authToken = await getAuthToken();
            const headers = authToken ? { 'Authorization': `Bearer ${authToken}` } : {};

            for (const file of files) {
                // Images route to /api/images (10 MB, image types), everything
                // else to /api/files (100 MB, whitelist). Guard against each
                // endpoint's real limits before uploading; server stays authoritative.
                const isImage = file.type.startsWith('image/');
                const notice = validateUpload(file, isImage ? 'image' : 'file');
                if (notice) {
                    showUploadNotice(notice);
                    console.warn('[tiptap] Dropped file rejected by client guard', { file: file.name, type: file.type, size: file.size });
                    continue;
                }

                const formData = new FormData();
                formData.append('file', file);
                const toast = createProgressToast(file.name);

                try {
                    if (isImage) {
                        // Image files are inserted as inline images
                        const { url } = await uploadWithProgress(
                            `${apiBaseUrl}/api/images`, formData, headers,
                            (ratio) => toast.update(ratio)
                        );
                        toast.done();
                        console.debug(`[tiptap] Image dropped and uploaded: ${file.name} (${file.size}B) -> ${url}`);
                        editor.chain().focus().insertContentAt(insertPos, {
                            type: 'image',
                            attrs: { src: `${apiBaseUrl}${url}`, class: 'tiptap-image' },
                        }).run();
                        // Resolve the newly inserted <img> to a Blob URL immediately.
                        const entry = _editors[elementId];
                        if (entry) {
                            await resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, getAuthToken, entry.blobUrls, entry.blobUrlCache);
                        }
                    } else {
                        // Other files are inserted as file attachment links
                        const { url, filename } = await uploadWithProgress(
                            `${apiBaseUrl}/api/files`, formData, headers,
                            (ratio) => toast.update(ratio)
                        );
                        toast.done();
                        console.debug(`[tiptap] File dropped and uploaded: ${file.name} (${file.size}B) -> ${url}`);
                        editor.chain().focus().insertContentAt(insertPos, {
                            type: 'fileAttachment',
                            attrs: { href: `${apiBaseUrl}${url}`, filename },
                        }).run();
                    }
                } catch (err) {
                    toast.error();
                    console.error('[tiptap] Drop file upload failed', { file: file.name, size: file.size, error: err.message });
                }
            }
        });

        const blobUrls = [];
        const blobUrlCache = new Map();
        _editors[elementId] = { editor, bubbleMenuEl, linkPopover, apiBaseUrl, getAuthToken, blobUrls, blobUrlCache };
        console.debug(`[tiptap] Editor initialized: ${elementId}`);

        // Re-apply blob URLs after every doc-mutating transaction. ProseMirror's
        // view layer patches <img src> back to the canonical /api/images/* URL
        // whenever it re-renders the node (e.g., pressing Enter near the image),
        // which makes the browser try to load the URL without a JWT and the
        // image looks broken. The cache hit path is synchronous and fires no
        // network calls, so this is cheap on the keystroke hot path.
        editor.on('update', () => {
            resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, getAuthToken, blobUrls, blobUrlCache);
        });

        // Resolve any images that were injected with initialContent.
        if (initialContent) {
            await resolveAuthenticatedImages(editor.view.dom, apiBaseUrl, getAuthToken, blobUrls, blobUrlCache);
        }
    },

    focus(elementId) {
        _editors[elementId]?.editor.commands.focus('start');
    },

    // Toggle read-only mode (used for archived notes).
    setEditable(elementId, editable) {
        _editors[elementId]?.editor.setEditable(editable);
    },

    async setContent(elementId, content) {
        const entry = _editors[elementId];
        if (!entry) return;
        // Revoke previous blob URLs before the DOM is replaced with new content,
        // and clear the cache so stale apiUrl → blobUrl entries don't linger.
        for (const url of entry.blobUrls) URL.revokeObjectURL(url);
        entry.blobUrls = [];
        entry.blobUrlCache.clear();
        entry.editor.commands.setContent(content, false);
        await resolveAuthenticatedImages(entry.editor.view.dom, entry.apiBaseUrl, entry.getAuthToken, entry.blobUrls, entry.blobUrlCache);
    },

    setInputValue(elementId, value) {
        const el = document.getElementById(elementId);
        if (el) el.value = value;
    },

    updateMentionTitle(elementId, noteId, newTitle) {
        const entry = _editors[elementId];
        if (!entry) return null;
        const { editor } = entry;
        let found = false;
        editor.state.doc.descendants((node, pos) => {
            if (found) return false;
            if (node.type.name === 'noteMention' && node.attrs.noteId === noteId) {
                editor.view.dispatch(
                    editor.state.tr.setNodeMarkup(pos, null, { ...node.attrs, title: newTitle })
                );
                found = true;
                return false;
            }
        });
        return found ? editor.getHTML() : null;
    },

    updatePageLink(elementId, noteId, newTitle) {
        const entry = _editors[elementId];
        if (!entry) return null;
        const { editor } = entry;
        let found = false;
        editor.state.doc.descendants((node, pos) => {
            if (found) return false;
            if (node.type.name === 'pageLink' && node.attrs.noteId === noteId) {
                editor.view.dispatch(
                    editor.state.tr.setNodeMarkup(pos, null, { ...node.attrs, title: newTitle })
                );
                found = true;
                return false;
            }
        });
        // Read HTML from the updated state
        return found ? editor.getHTML() : null;
    },

    getMarkdown(elementId) {
        return _editors[elementId]?.editor.storage.markdown.getMarkdown() ?? '';
    },

    downloadMarkdown(elementId, filename) {
        const md = _editors[elementId]?.editor.storage.markdown.getMarkdown() ?? '';
        const blob = new Blob([md], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },

    // Render all mermaid blocks found inside a DOM element (for read-only views).
    // Pass a CSS selector string or a DOM element reference.
    async renderMermaidIn(root) {
        const el = typeof root === 'string' ? document.querySelector(root) : root;
        if (!el) return;
        const blocks = el.querySelectorAll('pre[data-type="mermaid"]');
        if (!blocks.length) return;
        // Eagerly load mermaid so any module-load error surfaces early
        try { await getMermaid(); } catch (err) {
            console.error('[tiptap] Failed to load Mermaid for read-only render', err);
            return;
        }
        for (const block of blocks) {
            const code = block.querySelector('code')?.textContent?.trim();
            if (!code) continue;
            try {
                const container = document.createElement('div');
                container.className = 'mermaid-rendered';
                container.innerHTML = await renderMermaidSvg(code);
                block.replaceWith(container);
            } catch (err) {
                const errDiv = document.createElement('div');
                errDiv.className = 'mermaid-error';
                errDiv.textContent = `⚠ ${err.message || 'Diagram error'}`;
                block.replaceWith(errDiv);
            }
        }
    },

    // Replace authenticated media that anonymous viewers cannot load with an
    // inline placeholder (for the read-only Share page). Embedded images and
    // file attachments reference authenticated endpoints (/api/images/*,
    // /api/files/*) that return 401 without a JWT, so images render as the
    // browser's broken-image icon and attachment links download nothing. Swap
    // those for a friendly placeholder / notice. Pass a CSS selector string or
    // a DOM element reference. Runs after the sanitized content is in the DOM,
    // mirroring renderMermaidIn.
    applySharedPlaceholders(root) {
        const el = typeof root === 'string' ? document.querySelector(root) : root;
        if (!el) return;

        // Images: swap on load failure, and immediately for any that already
        // finished loading and failed before this handler was attached.
        for (const img of el.querySelectorAll('img')) {
            const replace = () => {
                if (!img.isConnected) return;
                const placeholder = document.createElement('div');
                placeholder.className = 'shared-media-placeholder';
                placeholder.textContent = 'Image not available in shared view';
                img.replaceWith(placeholder);
            };
            img.addEventListener('error', replace, { once: true });
            if (img.complete && img.naturalWidth === 0) replace();
        }

        // Attachments cannot be downloaded anonymously — disable the link and
        // append an explicit notice so the reader is not left clicking a dead link.
        for (const link of el.querySelectorAll('a.file-attachment')) {
            link.classList.add('shared-attachment-unavailable');
            link.removeAttribute('href');
            link.removeAttribute('download');
            link.setAttribute('aria-disabled', 'true');
            const note = document.createElement('span');
            note.className = 'shared-attachment-note';
            note.textContent = ' (not available in shared view)';
            link.appendChild(note);
        }
    },

    // Make Toggle / Accordion blocks interactive in a read-only shared view. The
    // shared content is a raw HTML dump, so the Tiptap node view never runs: toggles
    // have no arrow, no `.toggle-inner` wrapper (which the folding CSS keys off of),
    // and no click handler, leaving a collapsed body broken and unreadable (issue
    // #274). Rebuild the exact node-view DOM structure so the existing app.css rules
    // apply (arrow rotation + `[data-open="false"] > .toggle-inner >
    // [data-type="toggle-content"] { display:none }`) and wire a click to flip
    // data-open. Accordions are just groups of toggle blocks, so they are covered
    // too; the editor-only "one open at a time" constraint is intentionally not
    // enforced on this read-only page. Pass a CSS selector string or a DOM element.
    enableSharedToggles(root) {
        const el = typeof root === 'string' ? document.querySelector(root) : root;
        if (!el) return;

        // The server sanitizer strips the `class` attribute from stored content, so the
        // shared dump keeps `data-type`/`data-open` but loses `toggle-block`, `toggle-summary`,
        // `toggle-content`, `accordion-group` — the exact classes app.css keys its folding,
        // flex layout and arrow rotation on. Restore them here (data-* survives, so we can
        // re-derive every class) so the existing CSS applies without touching the sanitizer (#274).
        for (const group of el.querySelectorAll('[data-type="accordion-group"]'))
            group.classList.add('accordion-group');

        for (const toggle of el.querySelectorAll('[data-type="toggle"]')) {
            // Defensive: skip anything already rebuilt (e.g. a double invocation).
            if (toggle.querySelector(':scope > .toggle-inner')) continue;

            const summary = toggle.querySelector(':scope > [data-type="toggle-summary"]');
            const content = toggle.querySelector(':scope > [data-type="toggle-content"]');
            if (!summary || !content) continue;

            const open = toggle.getAttribute('data-open') !== 'false';
            toggle.setAttribute('data-open', open ? 'true' : 'false');

            // Restore the sanitizer-stripped classes the folding/layout CSS depends on.
            toggle.classList.add('toggle-block');
            summary.classList.add('toggle-summary');
            content.classList.add('toggle-content');

            const arrow = document.createElement('button');
            arrow.type = 'button';
            arrow.className = 'toggle-arrow';
            arrow.setAttribute('aria-expanded', String(open));
            arrow.setAttribute('aria-label', 'Toggle section');
            arrow.textContent = '▸'; // ▸

            // Wrap summary + content in `.toggle-inner` and prepend the arrow,
            // matching the editor node view so the existing CSS layout/folding works.
            const inner = document.createElement('div');
            inner.className = 'toggle-inner';
            inner.append(summary, content);
            toggle.prepend(arrow);
            toggle.append(inner);

            const flip = () => {
                const willOpen = toggle.getAttribute('data-open') === 'false';
                toggle.setAttribute('data-open', willOpen ? 'true' : 'false');
                arrow.setAttribute('aria-expanded', String(willOpen));
                // Accordion parity: opening a toggle inside an accordion group collapses
                // its siblings, mirroring the editor's one-open-at-a-time behavior (#274).
                if (willOpen) {
                    const group = toggle.closest('[data-type="accordion-group"]');
                    if (group) {
                        for (const sib of group.querySelectorAll(':scope > [data-type="toggle"]')) {
                            if (sib === toggle) continue;
                            sib.setAttribute('data-open', 'false');
                            const sibArrow = sib.querySelector(':scope > .toggle-arrow');
                            if (sibArrow) sibArrow.setAttribute('aria-expanded', 'false');
                        }
                    }
                }
            };
            arrow.addEventListener('click', flip);
            // A read-only page can't edit the summary, so make the whole summary a
            // click target too (except real links inside it) for an easier hit area.
            summary.style.cursor = 'pointer';
            summary.addEventListener('click', e => {
                if (e.target.closest('a')) return;
                flip();
            });
        }
    },

    destroy(elementId) {
        const entry = _editors[elementId];
        if (entry) {
            hideCalloutPicker();
            for (const url of entry.blobUrls) URL.revokeObjectURL(url);
            entry.blobUrlCache.clear();
            entry.editor.destroy();
            entry.bubbleMenuEl.remove();
            document.removeEventListener('mousedown', entry.linkPopover.onDocumentMouseDown);
            entry.linkPopover.popover.remove();
            delete _editors[elementId];
            console.debug(`[tiptap] Editor destroyed: ${elementId}`);
        }
    },
};
