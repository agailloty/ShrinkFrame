let zone;
let picker;
let dotnet;
let selected = new Map();
let token;

export function initialize(element, reference) {
    zone = element;
    picker = zone.querySelector('[data-upload-picker]');
    dotnet = reference;
    picker.addEventListener('change', () => addFiles(picker.files));
    zone.addEventListener('dragover', event => { event.preventDefault(); zone.classList.add('is-dragging'); });
    zone.addEventListener('dragleave', () => zone.classList.remove('is-dragging'));
    zone.addEventListener('drop', event => { event.preventDefault(); zone.classList.remove('is-dragging'); addFiles(event.dataTransfer.files); });
    zone.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); picker.click(); } });
}

export function openPicker() { picker.click(); }

function addFiles(fileList) {
    const added = [];
    for (const file of fileList) {
        const id = crypto.randomUUID();
        selected.set(id, file);
        added.push({ clientId: id, fileName: file.name, size: file.size });
    }
    dotnet.invokeMethodAsync('FilesSelected', added);
    picker.value = '';
}

async function securityToken() {
    if (token) return token;
    const response = await fetch('/api/browser-uploads/antiforgery', { credentials: 'same-origin' });
    token = (await response.json()).requestToken;
    return token;
}

async function ensureBatch(name) {
    let batchId = sessionStorage.getItem('shrinkframe.browserBatchId');
    if (batchId) return batchId;
    const response = await fetch('/api/browser-batches/', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': await securityToken() },
        body: JSON.stringify({ name })
    });
    const body = await response.json();
    if (!response.ok) throw body;
    batchId = body.batchId;
    sessionStorage.setItem('shrinkframe.browserBatchId', batchId);
    return batchId;
}

export async function uploadAll(name) {
    try {
        const batchId = await ensureBatch(name);
        await Promise.all([...selected].map(([id, file]) => uploadOne(batchId, id, file)));
    } catch (error) {
        await dotnet.invokeMethodAsync('PageError', error.errorCode || 'upload.failed', error.errorMessage || 'The upload could not be started.');
    }
}

export async function retry(id, name) {
    const file = selected.get(id);
    if (!file) return;
    try { await uploadOne(await ensureBatch(name), id, file); }
    catch (error) { await dotnet.invokeMethodAsync('PageError', 'upload.failed', error.message || 'Retry failed.'); }
}

function uploadOne(batchId, id, file) {
    return new Promise(async resolve => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', `/api/browser-batches/${batchId}/files`);
        xhr.setRequestHeader('Content-Type', file.type || 'application/octet-stream');
        xhr.setRequestHeader('X-ShrinkFrame-File-Name', encodeURIComponent(file.name));
        xhr.setRequestHeader('RequestVerificationToken', await securityToken());
        xhr.upload.onprogress = event => dotnet.invokeMethodAsync('UploadProgress', id, event.lengthComputable ? Math.round(event.loaded * 100 / event.total) : 0);
        xhr.onload = async () => {
            let body;
            try { body = JSON.parse(xhr.responseText); } catch { body = { errorCode: 'upload.failed', errorMessage: 'The server returned an unreadable response.' }; }
            await dotnet.invokeMethodAsync('UploadCompleted', id, body.state || 'Failed', body.errorCode, body.errorMessage);
            if (!body.errorCode) selected.delete(id);
            resolve();
        };
        xhr.onerror = async () => { await dotnet.invokeMethodAsync('UploadCompleted', id, 'Failed', 'upload.connection_lost', 'The connection was lost; retry starts from zero.'); resolve(); };
        xhr.onabort = async () => { await dotnet.invokeMethodAsync('UploadCompleted', id, 'Failed', 'upload.aborted', 'The upload was aborted; retry starts from zero.'); resolve(); };
        xhr.send(file);
    });
}

export function remove(id) { selected.delete(id); }

export async function restore() {
    const batchId = sessionStorage.getItem('shrinkframe.browserBatchId');
    if (!batchId) return;
    const response = await fetch(`/api/browser-batches/${batchId}`, { credentials: 'same-origin' });
    if (response.ok) await dotnet.invokeMethodAsync('ServerState', await response.json());
    else if (response.status === 404) sessionStorage.removeItem('shrinkframe.browserBatchId');
}
