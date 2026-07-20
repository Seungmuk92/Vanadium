// ── Upload helpers ───────────────────────────────────────────────────────────

// ── Client-side pre-upload guard ─────────────────────────────────────────────
// Rejects oversized / unsupported files before any bytes leave the browser, so
// the user gets instant feedback instead of streaming (up to) 100 MB only to be
// refused by the server. This is an ADDITIONAL defense line: the server checks
// (magic-byte sniffing, size limits) remain authoritative and are never relaxed.
//
// Keep these in sync with the server:
//   - ImagesController: 10 MB cap, JPEG/PNG/GIF/WebP
//   - FilesController:  100 MB cap, AllowedContentTypes whitelist
export const IMAGE_MAX_BYTES = 10 * 1024 * 1024;
export const FILE_MAX_BYTES = 100 * 1024 * 1024;

export const IMAGE_CONTENT_TYPES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];

export const FILE_CONTENT_TYPES = [
    'application/pdf',
    'application/zip',
    'application/msword',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    'application/vnd.ms-excel',
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    'text/plain',
    'text/markdown',
    'image/jpeg',
    'image/png',
    'image/gif',
    'image/webp',
];

function formatMb(bytes) {
    return `${Math.round(bytes / (1024 * 1024))} MB`;
}

// Validate a file for the given upload kind ('image' → /api/images,
// 'file' → /api/files). Returns null when acceptable, otherwise a user-facing
// rejection message. Matching on file.type mirrors the server's Content-Type
// whitelist check (the browser sends the same type it reports here).
export function validateUpload(file, kind) {
    const isImage = kind === 'image';
    const maxBytes = isImage ? IMAGE_MAX_BYTES : FILE_MAX_BYTES;
    const allowed = isImage ? IMAGE_CONTENT_TYPES : FILE_CONTENT_TYPES;

    if (!allowed.includes(file.type)) {
        return `"${file.name}" was not uploaded: unsupported ${isImage ? 'image' : 'file'} type.`;
    }
    if (file.size > maxBytes) {
        return `"${file.name}" was not uploaded: exceeds the ${formatMb(maxBytes)} limit.`;
    }
    return null;
}

// Transient bottom-right notice for a rejected upload. Reuses the upload-toast
// look (error variant) but carries a full wrapped message instead of a progress
// bar; auto-dismisses after a few seconds.
export function showUploadNotice(message) {
    const toast = document.createElement('div');
    toast.className = 'upload-toast upload-toast-notice';

    const msgEl = document.createElement('span');
    msgEl.className = 'upload-toast-notice-msg';
    msgEl.textContent = message;

    toast.appendChild(msgEl);
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 4000);
}

export function uploadWithProgress(url, formData, headers, onProgress) {
    return new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', url);
        for (const [key, value] of Object.entries(headers)) {
            xhr.setRequestHeader(key, value);
        }
        xhr.upload.addEventListener('progress', (e) => {
            if (e.lengthComputable) onProgress(e.loaded / e.total);
        });
        xhr.addEventListener('load', () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                resolve(JSON.parse(xhr.responseText));
            } else {
                reject(new Error(`Upload failed: ${xhr.status}`));
            }
        });
        xhr.addEventListener('error', () => reject(new Error('Network error')));
        xhr.send(formData);
    });
}

export function createProgressToast(filename) {
    const toast = document.createElement('div');
    toast.className = 'upload-toast';

    // Only the filename is user-derived; insert it via textContent so it is
    // never interpreted as HTML. The rest is fixed structure.
    const nameEl = document.createElement('span');
    nameEl.className = 'upload-toast-name';
    nameEl.textContent = filename;

    const barWrap = document.createElement('div');
    barWrap.className = 'upload-toast-bar-wrap';
    const bar = document.createElement('div');
    bar.className = 'upload-toast-bar';
    barWrap.appendChild(bar);

    const pct = document.createElement('span');
    pct.className = 'upload-toast-pct';
    pct.textContent = '0%';

    toast.append(nameEl, barWrap, pct);
    document.body.appendChild(toast);

    return {
        update(ratio) {
            toast.querySelector('.upload-toast-bar').style.width = `${Math.round(ratio * 100)}%`;
            toast.querySelector('.upload-toast-pct').textContent = `${Math.round(ratio * 100)}%`;
        },
        done() {
            toast.classList.add('upload-toast-done');
            setTimeout(() => toast.remove(), 1200);
        },
        error() {
            toast.classList.add('upload-toast-error');
            setTimeout(() => toast.remove(), 3000);
        },
    };
}
