// Entry point for the Tiptap editor integration.
//
// The former 2000-line monolith has been split into focused ES modules under
// ./tiptap/. This file stays the single entry referenced by index.html
// (<script type="module" src="js/tiptap-editor.js">) so the load path and the
// public `window.tiptapInterop` contract are unchanged. Importing interop.js
// installs `window.tiptapInterop` and pulls in every node/extension/UI module
// it depends on.
import './tiptap/interop.js';

// interop.js resolves its dependencies from a CDN, so the static import above
// only completes once those async imports finish — at which point
// window.tiptapInterop has been installed. Expose a `ready()` gate on the same
// interop object so callers can await the module load instead of racing it.
// This matters for pages that invoke the interop straight after their first
// render and never initialize an editor themselves — notably the anonymous
// /share view (issue #242), which otherwise sees `tiptapInterop` undefined on a
// cold load. The gate resolves immediately because this entry module's body
// runs only after interop.js (and its imports) have fully loaded.
window.tiptapInterop.ready = () => Promise.resolve(true);
