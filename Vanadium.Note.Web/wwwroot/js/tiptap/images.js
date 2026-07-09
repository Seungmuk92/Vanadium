// ── Authenticated image loading ───────────────────────────────────────────────
//
// After Tiptap renders content, <img> tags carry the original /api/images/{id}
// URLs.  The browser cannot load those directly now that GET requires a JWT.
// This function fetches each matching image with the Authorization header,
// creates an object URL from the response blob, and swaps it into the DOM src.
// The ProseMirror model is deliberately NOT touched — editor.getHTML() still
// returns the canonical API URL, which is what gets persisted.
//
// ProseMirror's view layer re-renders <img> nodes whenever the document
// changes (e.g., pressing Enter near an image), which patches the DOM src
// back to the canonical API URL. To keep the image visible across those
// re-renders we maintain a per-editor blobUrlCache (apiUrl → blobUrl) and
// reapply the cached blob URL synchronously on cache hit, so the typical
// keystroke path makes no network calls.
//
// blobUrls is a per-editor array used for revocation; callers are
// responsible for revoking its entries (and clearing blobUrlCache) when the
// editor is destroyed or content is replaced.

// getAuthToken is an async callback that returns the CURRENT token at call
// time (not a value captured when the editor was created), so a re-login after
// JWT expiry is picked up here without re-initializing the editor (issue #126).
// It is invoked lazily, only on a cache miss that actually needs the network,
// so the cache-hit hot path stays free of Blazor interop round-trips.
export async function resolveAuthenticatedImages(editorDom, apiBaseUrl, getAuthToken, blobUrls, blobUrlCache) {
    const prefix = `${apiBaseUrl}/api/images/`;
    const imgs = editorDom.querySelectorAll(`img[src^="${CSS.escape(prefix)}"], img[src^="${prefix}"]`);
    let headers = null;

    for (const img of imgs) {
        const src = img.getAttribute('src');
        if (!src || !src.startsWith(prefix)) continue;

        // Cache hit — reapply the existing blob URL without a network fetch.
        // This is the hot path when ProseMirror re-renders an image after an
        // unrelated transaction (e.g., line break above the image).
        const cached = blobUrlCache.get(src);
        if (cached) {
            img.src = cached;
            continue;
        }

        // First real fetch this pass — resolve the current token once and reuse
        // its headers for the remaining images.
        if (headers === null) {
            const authToken = await getAuthToken();
            headers = authToken ? { 'Authorization': `Bearer ${authToken}` } : {};
        }

        try {
            const response = await fetch(src, { headers });
            if (!response.ok) {
                console.warn(`[tiptap] Image auth fetch failed: ${src} (HTTP ${response.status})`);
                continue;
            }
            const blob = await response.blob();
            const blobUrl = URL.createObjectURL(blob);
            blobUrls.push(blobUrl);
            blobUrlCache.set(src, blobUrl);
            img.src = blobUrl;
        } catch (err) {
            console.error('[tiptap] Image fetch error', { src, error: err.message });
        }
    }
}
