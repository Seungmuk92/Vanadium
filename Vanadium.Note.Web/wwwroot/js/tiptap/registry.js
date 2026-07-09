// ── Editor registry ──────────────────────────────────────────────────────────
//
// Shared map of live editor instances, keyed by DOM element id. Each entry
// holds the Editor plus its associated UI elements and per-editor state
// (blob URL cache, auth token resolver). Split modules import this so the
// link popover, bubble menu, and public interop API all address the same
// registry.

export const editors = {};
