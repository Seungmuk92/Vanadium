// ── Mermaid lazy-loader ──────────────────────────────────────────────────────

let _mermaidPromise = null;
let _mermaidIdCounter = 0;

export async function getMermaid() {
    if (!_mermaidPromise) {
        _mermaidPromise = import('https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.esm.min.mjs')
            .then(m => m.default)
            .catch(err => {
                _mermaidPromise = null;
                throw err;
            });
    }
    return _mermaidPromise;
}

// Render a mermaid diagram to SVG, applying the correct theme for the
// current light/dark mode.  Always re-initializes before rendering so
// that a mode switch mid-session is reflected without a page reload.
export async function renderMermaidSvg(code) {
    const mermaid = await getMermaid();
    const isDark = document.body.classList.contains('dark-mode');
    mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: isDark ? 'dark' : 'default',
    });
    const id = `mermaid-${++_mermaidIdCounter}`;
    const { svg } = await mermaid.render(id, code);
    return svg;
}
