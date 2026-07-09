// ── Upload helpers ───────────────────────────────────────────────────────────

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
