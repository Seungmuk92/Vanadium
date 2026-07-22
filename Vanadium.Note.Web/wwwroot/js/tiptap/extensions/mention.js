import { Extension } from 'https://esm.sh/@tiptap/core@2'
import { PluginKey, Plugin } from 'https://esm.sh/prosemirror-state@1.4.4'
import Suggestion from 'https://esm.sh/@tiptap/suggestion@2'

// ── Mention extension (@) ────────────────────────────────────────────────────

const MENTION_SEARCH_DEBOUNCE_MS = 150;

// Wraps the mention search so it only fires ~150ms after the last keystroke,
// instead of once per keystroke. Each call supersedes any pending call; a late
// API response whose query was already superseded is ignored so the menu never
// renders stale results and the newest query alone drives the list.
function createDebouncedSearch(dotnetRef, delayMs) {
    let timer = null;
    let latestToken = 0;
    return query => {
        if (timer) clearTimeout(timer);
        const token = ++latestToken;
        return new Promise(resolve => {
            timer = setTimeout(async () => {
                timer = null;
                try {
                    const results = await dotnetRef.invokeMethodAsync('SearchNotes', query);
                    if (token === latestToken) resolve(results);
                } catch {
                    if (token === latestToken) resolve([]);
                }
            }, delayMs);
        });
    };
}

export function createMentionExtension(dotnetRef) {
    const searchNotes = createDebouncedSearch(dotnetRef, MENTION_SEARCH_DEBOUNCE_MS);
    return Extension.create({
        name: 'mention',
        addProseMirrorPlugins() {
            return [
                Suggestion({
                    editor: this.editor,
                    pluginKey: new PluginKey('mention'),
                    char: '@',
                    allowSpaces: true,
                    items: ({ query }) => searchNotes(query),
                    render() {
                        let menu = null;
                        let selectedIndex = 0;
                        let currentItems = [];
                        let currentCommand = null;

                        const renderItems = () => {
                            if (!menu) return;
                            menu.innerHTML = '';
                            if (!currentItems.length) {
                                const empty = document.createElement('div');
                                empty.className = 'mention-menu-empty';
                                empty.textContent = 'No notes found';
                                menu.appendChild(empty);
                                return;
                            }
                            currentItems.forEach((item, i) => {
                                const row = document.createElement('div');
                                row.className = 'mention-menu-item' + (i === selectedIndex ? ' mention-menu-item-active' : '');
                                // Build via textContent so a note title like
                                // `<img src=x onerror=...>` is rendered as text, not HTML.
                                const icon = document.createElement('span');
                                icon.className = 'mention-menu-icon';
                                icon.textContent = '📄';
                                const title = document.createElement('span');
                                title.className = 'mention-menu-title';
                                title.textContent = item.title || '(Untitled)';
                                row.append(icon, title);
                                row.addEventListener('mousedown', e => {
                                    e.preventDefault();
                                    currentCommand?.(item);
                                });
                                menu.appendChild(row);
                            });
                            menu.querySelector('.mention-menu-item-active')
                                ?.scrollIntoView({ block: 'nearest' });
                        };

                        const reposition = clientRect => {
                            if (!menu || !clientRect) return;
                            const rect = clientRect();
                            if (!rect) return;
                            const menuWidth = 240;
                            const itemCount = currentItems.length || 1;
                            const menuHeight = Math.min(itemCount * 36 + 8, 280);
                            const spaceBelow = window.innerHeight - rect.bottom;
                            const top = spaceBelow < menuHeight && rect.top > menuHeight
                                ? rect.top - menuHeight - 4
                                : rect.bottom + 4;
                            const left = Math.min(rect.left, window.innerWidth - menuWidth - 8);
                            menu.style.top  = `${top}px`;
                            menu.style.left = `${Math.max(8, left)}px`;
                        };

                        return {
                            onStart(props) {
                                selectedIndex = 0;
                                currentItems = props.items;
                                currentCommand = props.command;
                                menu = document.createElement('div');
                                menu.className = 'mention-menu';
                                document.body.appendChild(menu);
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onUpdate(props) {
                                selectedIndex = 0;
                                currentItems = props.items;
                                currentCommand = props.command;
                                renderItems();
                                reposition(props.clientRect);
                            },
                            onKeyDown({ event }) {
                                // Ignore keys fired during IME composition (e.g. Hangul).
                                // The Enter that confirms a composition must not be consumed
                                // as a menu selection (#279).
                                if (event.isComposing || event.keyCode === 229) return false;
                                if (event.key === 'ArrowUp') {
                                    if (!currentItems.length) return false;
                                    event.preventDefault();
                                    selectedIndex = (selectedIndex - 1 + currentItems.length) % currentItems.length;
                                    renderItems();
                                    return true;
                                }
                                if (event.key === 'ArrowDown') {
                                    if (!currentItems.length) return false;
                                    event.preventDefault();
                                    selectedIndex = (selectedIndex + 1) % currentItems.length;
                                    renderItems();
                                    return true;
                                }
                                if (event.key === 'Enter') {
                                    const item = currentItems[selectedIndex];
                                    if (item) { currentCommand?.(item); return true; }
                                    return false;
                                }
                                return false;
                            },
                            onExit() {
                                menu?.remove();
                                menu = null;
                            },
                        };
                    },
                    command({ editor, range, props: item }) {
                        editor
                            .chain()
                            .focus()
                            .deleteRange(range)
                            .insertContent({
                                type: 'noteMention',
                                attrs: { noteId: item.id, title: item.title || '' },
                            })
                            .insertContent(' ')
                            .run();
                    },
                }),
                new Plugin({
                    key: new PluginKey('mentionClick'),
                    props: {
                        handleClick(view, pos, event) {
                            const mention = event.target?.closest?.('a.note-mention');
                            if (!mention) return false;
                            event.preventDefault();
                            const noteId = mention.getAttribute('data-note-id');
                            if (noteId) {
                                dotnetRef.invokeMethodAsync('OnMentionClick', noteId)
                                    .catch(err => console.error('[tiptap] OnMentionClick failed', err));
                            }
                            return true;
                        },
                    },
                }),
            ];
        },
    });
}
