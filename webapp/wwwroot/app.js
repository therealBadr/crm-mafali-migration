// Plain scroll-position listener — deliberately not using Blazor's built-in
// <Virtualize ItemsProvider> mechanism. That mechanism (IntersectionObserver
// + JS interop callback into an ItemsProvider delegate) was confirmed, via
// real-browser testing with no JS errors and no failed network requests, to
// simply never invoke its provider in this app — the query that would
// supply rows never even runs. Every other grid in this app uses
// <Virtualize Items="list">, which only needs a plain, static list and has
// none of that machinery — proven reliable everywhere it's used. This file
// is the standalone replacement for the on-demand-loading half of that
// mechanism: fire a callback when the user scrolls near the bottom of a
// container, nothing more.
window.mafaliScroll = {
    attach: function (element, dotNetRef) {
        if (!element) return;
        const handler = () => {
            const nearBottom = element.scrollTop + element.clientHeight >= element.scrollHeight - 400;
            if (nearBottom) {
                dotNetRef.invokeMethodAsync('OnScrolledNearBottom');
            }
        };
        element.addEventListener('scroll', handler, { passive: true });
        element._mafaliScrollHandler = handler;
    },
    detach: function (element) {
        if (element && element._mafaliScrollHandler) {
            element.removeEventListener('scroll', element._mafaliScrollHandler);
            delete element._mafaliScrollHandler;
        }
    }
};

// Blazor Server has no filesystem/download API of its own — a component can
// only push data to the browser over the SignalR circuit, not trigger a
// real file save. This is the standard workaround: the server streams bytes
// via a DotNetStreamReference, and this function turns that stream into a
// Blob + a synthetic <a download> click, exactly like the old React app's
// client-side export used to do directly (there, the data was already in
// browser memory; here it has to arrive over the circuit first).
window.mafaliFileDownload = {
    downloadFileFromStream: async function (fileName, contentType, streamRef) {
        const arrayBuffer = await streamRef.arrayBuffer();
        const blob = new Blob([arrayBuffer], { type: contentType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    }
};

// Excel-style column resize for the .header-row/.cell grids used throughout
// this app. Deliberately does the drag itself entirely client-side (moving
// a guide line, not the real column) instead of calling back into Blazor on
// every mousemove — a Blazor Server round-trip per pixel of mouse movement
// would be visibly laggy over the SignalR connection. Only the final width,
// on mouseup, goes to .NET (one round-trip), which then re-renders every
// cell in that column — header and every currently-mounted row — from the
// updated width, same as any other server-side state change in this app.
window.mafaliColumnResize = {
    attach: function (headerRow, dotNetRef) {
        if (!headerRow) return;

        let drag = null;

        const widthAt = (clientX) => Math.max(40, Math.round(drag.startWidth + (clientX - drag.startX)));

        const onMouseDown = (e) => {
            const handle = e.target.closest('.col-resize-handle');
            if (!handle) return;
            e.preventDefault();
            e.stopPropagation();

            const gridHeight = headerRow.closest('.table-wrap')?.getBoundingClientRect().height
                ?? headerRow.getBoundingClientRect().height;

            const ghost = document.createElement('div');
            ghost.className = 'col-resize-ghost';
            ghost.style.left = e.clientX + 'px';
            ghost.style.top = headerRow.getBoundingClientRect().top + 'px';
            ghost.style.height = gridHeight + 'px';
            document.body.appendChild(ghost);

            drag = {
                key: handle.dataset.colKey,
                startX: e.clientX,
                startWidth: handle.closest('.cell').getBoundingClientRect().width,
                ghost: ghost,
            };
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        };

        const onMouseMove = (e) => {
            if (!drag) return;
            const newWidth = widthAt(e.clientX);
            drag.ghost.style.left = (drag.startX + (newWidth - drag.startWidth)) + 'px';
        };

        const onMouseUp = (e) => {
            if (!drag) return;
            const newWidth = widthAt(e.clientX);
            document.body.removeChild(drag.ghost);
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            const key = drag.key;
            drag = null;
            dotNetRef.invokeMethodAsync('OnColumnResized', key, newWidth);
        };

        // Double-click a handle to auto-fit, same as Excel/Sheets. The data
        // itself never leaves the server as a client-side model in Blazor
        // Server, so this asks .NET for this one column's text (respecting
        // whatever search filter is currently applied — "longest content"
        // means longest of what's actually showing), then measures it here
        // with a canvas using the grid's real fonts, since only the browser
        // knows how wide a given string actually renders.
        const measureAutoFitWidth = (header, values, sampleCell, sampleHeaderCell) => {
            const canvas = measureAutoFitWidth._canvas || (measureAutoFitWidth._canvas = document.createElement('canvas'));
            const ctx = canvas.getContext('2d');

            const cellStyle = getComputedStyle(sampleCell);
            const headerStyle = getComputedStyle(sampleHeaderCell);
            const hPad = parseFloat(cellStyle.paddingLeft) + parseFloat(cellStyle.paddingRight);

            ctx.font = cellStyle.font;
            let maxContentWidth = 0;
            for (const v of values) {
                const w = ctx.measureText(v ?? '').width;
                if (w > maxContentWidth) maxContentWidth = w;
            }

            ctx.font = headerStyle.font;
            // Header text is uppercased via CSS (text-transform), not in the
            // underlying string — measure what's actually drawn.
            const headerWidth = ctx.measureText((header ?? '').toUpperCase()).width;

            return Math.max(40, Math.ceil(Math.max(maxContentWidth, headerWidth) + hPad + 4));
        };

        const onDblClick = async (e) => {
            const handle = e.target.closest('.col-resize-handle');
            if (!handle) return;
            e.preventDefault();
            e.stopPropagation();

            const key = handle.dataset.colKey;
            const headerCell = handle.closest('.cell');
            const sampleBodyCell = document.querySelector('.row .cell') || headerCell;

            const data = await dotNetRef.invokeMethodAsync('GetColumnAutoFitData', key);
            const newWidth = measureAutoFitWidth(data.header, data.values, sampleBodyCell, headerCell);
            dotNetRef.invokeMethodAsync('OnColumnResized', key, newWidth);
        };

        headerRow.addEventListener('mousedown', onMouseDown);
        headerRow.addEventListener('dblclick', onDblClick);
        headerRow._mafaliResizeMouseDownHandler = onMouseDown;
        headerRow._mafaliResizeDblClickHandler = onDblClick;
    },
    detach: function (headerRow) {
        if (!headerRow) return;
        if (headerRow._mafaliResizeMouseDownHandler) {
            headerRow.removeEventListener('mousedown', headerRow._mafaliResizeMouseDownHandler);
            delete headerRow._mafaliResizeMouseDownHandler;
        }
        if (headerRow._mafaliResizeDblClickHandler) {
            headerRow.removeEventListener('dblclick', headerRow._mafaliResizeDblClickHandler);
            delete headerRow._mafaliResizeDblClickHandler;
        }
    }
};

// Backs the Bon de Commande page's Destination section and (later) the
// "Générer fichier bon de commande" button on Parcours Client. The File
// System Access API's directory handle is a browser object with no
// server-side equivalent — it's inherently local to one browser profile,
// so it's persisted in this browser's own IndexedDB, not this app's
// database. Chrome/Edge only; isSupported() is how the Razor page decides
// whether to show the folder picker or fall back to a plain download.
window.mafaliBonCommandeDestination = {
    isSupported: function () {
        return 'showDirectoryPicker' in window;
    },

    // User-gesture-triggered (called from a Blazor @onclick handler) —
    // showDirectoryPicker() throws SecurityError outside a user activation
    // window, which the click → C# → JS round-trip stays inside of as long
    // as it resolves quickly (the standard, documented way Blazor Server
    // apps use this API). Returns null (not a thrown error) if the user
    // just cancels the native picker — that's not a failure.
    pickFolder: async function () {
        let handle;
        try {
            handle = await window.showDirectoryPicker({ mode: 'readwrite' });
        } catch (e) {
            if (e.name === 'AbortError') return null;
            throw e;
        }
        await this._storeHandle(handle);
        return handle.name;
    },

    // Display-only — does not re-verify the permission is still granted
    // (that happens at actual write time, in writeFile below). Returns
    // null if no folder has ever been granted.
    getCurrentFolderName: async function () {
        const handle = await this._getStoredHandle();
        return handle ? handle.name : null;
    },

    // Called from "Générer fichier bon de commande" once bytes are ready.
    // Returns a short status string instead of throwing, so the C# side
    // can decide what to show without needing exception-shaped plumbing
    // over JS interop for expected outcomes (no folder yet, permission
    // lost) vs genuine errors. Re-requests permission every time rather
    // than trusting the original grant — it can silently expire, and this
    // still happens inside the same click → C# → JS chain as the
    // template-choice button, so it stays inside the user-activation
    // window the same way pickFolder does.
    writeFile: async function (fileName, streamRef) {
        const handle = await this._getStoredHandle();
        if (!handle) return 'no-folder';

        let permission;
        try {
            permission = await handle.requestPermission({ mode: 'readwrite' });
        } catch (e) {
            return 'permission-error';
        }
        if (permission !== 'granted') return 'permission-denied';

        try {
            const arrayBuffer = await streamRef.arrayBuffer();
            const fileHandle = await handle.getFileHandle(fileName, { create: true });
            const writable = await fileHandle.createWritable();
            await writable.write(arrayBuffer);
            await writable.close();
            return 'ok';
        } catch (e) {
            return 'write-error';
        }
    },

    _dbPromise: null,
    _openDb: function () {
        if (this._dbPromise) return this._dbPromise;
        this._dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open('mafali-bon-commande', 1);
            req.onupgradeneeded = () => req.result.createObjectStore('handles');
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        return this._dbPromise;
    },
    _storeHandle: async function (handle) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('handles', 'readwrite');
            tx.objectStore('handles').put(handle, 'destinationFolder');
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    },
    _getStoredHandle: async function () {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('handles', 'readonly');
            const req = tx.objectStore('handles').get('destinationFolder');
            req.onsuccess = () => resolve(req.result || null);
            req.onerror = () => reject(req.error);
        });
    }
};
