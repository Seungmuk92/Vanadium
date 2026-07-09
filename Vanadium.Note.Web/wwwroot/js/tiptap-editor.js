// Entry point for the Tiptap editor integration.
//
// The former 2000-line monolith has been split into focused ES modules under
// ./tiptap/. This file stays the single entry referenced by index.html
// (<script type="module" src="js/tiptap-editor.js">) so the load path and the
// public `window.tiptapInterop` contract are unchanged. Importing interop.js
// installs `window.tiptapInterop` and pulls in every node/extension/UI module
// it depends on.
import './tiptap/interop.js';
